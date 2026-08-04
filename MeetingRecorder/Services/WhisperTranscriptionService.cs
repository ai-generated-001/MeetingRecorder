using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MeetingRecorder.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace MeetingRecorder.Services;

public class WhisperTranscriptionService : ITranscriptionService
{
    // Whisper works best with 6–10s windows; we slide forward by ChunkStepSamples
    // to emit results more frequently while keeping a longer context for accuracy.
    private const int SampleRate = 16000;
    private const int ChunkWindowSamples = SampleRate * 6;  // 6s window
    private const int ChunkStepSamples   = SampleRate * 2;  // emit every 2s
    private const float SilenceThreshold = 0.005f;          // RMS below this → skip

    private WhisperFactory?   _whisperFactory;
    private WhisperProcessor? _processor;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;
    private Channel<Memory<float>>? _audioChannel;

    // Ring-buffer accumulating resampled mono floats
    private readonly float[] _ringBuffer = new float[ChunkWindowSamples * 4];
    private int _ringHead;   // write position
    private int _ringCount;  // valid samples in buffer

    private readonly List<TranscriptionSegment> _fullTranscript = new();
    private TimeSpan _currentTimeOffset = TimeSpan.Zero;
    private string _currentLanguage = "auto";

    public bool IsTranscribing { get; private set; }

    public event EventHandler<TranscriptionSegmentEventArgs>? SegmentTranscribed;
    public event EventHandler<string>? StatusChanged;

    // ─── Model management ─────────────────────────────────────────────────────

    public async Task InitializeModelAsync(string modelSizeStr, string language = "auto")
    {
        _currentLanguage = language;
        await DownloadModelAsync(modelSizeStr, language);

        var modelType = ToGgmlType(modelSizeStr);
        var modelPath = GetModelPath(modelType);

        StatusChanged?.Invoke(this, "Loading model...");
        _whisperFactory?.Dispose();
        _whisperFactory = WhisperFactory.FromPath(modelPath);
        StatusChanged?.Invoke(this, "Model ready.");
    }

    public async Task DownloadModelAsync(string modelSizeStr, string language = "auto")
    {
        var modelType = ToGgmlType(modelSizeStr);
        var modelPath = GetModelPath(modelType);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);

        if (!File.Exists(modelPath))
        {
            StatusChanged?.Invoke(this, $"Downloading Whisper {modelType} model (this may take a while)...");
            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(modelType);
            using var fileWriter  = File.OpenWrite(modelPath);
            await modelStream.CopyToAsync(fileWriter);
            StatusChanged?.Invoke(this, $"Download complete: {modelType} model.");
        }
        else
        {
            StatusChanged?.Invoke(this, $"Whisper {modelType} model is already downloaded.");
        }
    }

    private static GgmlType ToGgmlType(string s) => s.ToLower() switch
    {
        "tiny"   => GgmlType.Tiny,
        "small"  => GgmlType.Small,
        "medium" => GgmlType.Medium,
        _        => GgmlType.Base
    };

    private static string GetModelPath(GgmlType modelType)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MeetingRecorder", "Models");
        return Path.Combine(dir, $"ggml-{modelType.ToString().ToLower()}.bin");
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public void StartTranscription()
    {
        if (IsTranscribing) return;
        if (_whisperFactory == null)
        {
            StatusChanged?.Invoke(this, "Error: Model not initialized.");
            return;
        }

        _fullTranscript.Clear();
        _currentTimeOffset = TimeSpan.Zero;
        _ringHead  = 0;
        _ringCount = 0;

        _processor?.Dispose();
        var builder = _whisperFactory.CreateBuilder()
            .WithThreads(Math.Max(1, Environment.ProcessorCount - 1)); // leave 1 core free

        if (!string.IsNullOrEmpty(_currentLanguage) && _currentLanguage != "auto")
            builder = builder.WithLanguage(_currentLanguage);

        _processor = builder.Build();

        // Bounded channel: if Whisper falls behind, oldest chunks are dropped
        // rather than letting memory grow without bound.
        _audioChannel = Channel.CreateBounded<Memory<float>>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        _cts = new CancellationTokenSource();
        IsTranscribing = true;

        _processingTask = Task.Run(() => ProcessAudioLoop(_cts.Token));
        StatusChanged?.Invoke(this, "Transcription started.");
    }

    public void StopTranscription()
    {
        if (!IsTranscribing) return;
        IsTranscribing = false;

        _audioChannel?.Writer.Complete();
        _cts?.Cancel();
        _processingTask?.Wait(TimeSpan.FromSeconds(10));

        _processor?.Dispose();
        _processor = null;
        _cts?.Dispose();
        _cts = null;
        StatusChanged?.Invoke(this, "Transcription stopped.");
    }

    // ─── Audio ingestion (called on the recorder thread every ~20ms) ──────────

    public void FeedAudioData(float[] samples, int count)
    {
        if (!IsTranscribing || _audioChannel == null) return;

        // --- Stereo 44100 Hz  →  mono 16000 Hz  (in one pass) ---
        // Decimation ratio: 44100 / 16000 ≈ 2.75625
        // For each output sample we linearly interpolate between input stereo pairs.
        int monoCount = count / 2;                                  // interleaved → mono length
        int outCount  = (int)(monoCount * 16000L / 44100L);
        if (outCount == 0) return;

        // Use ArrayPool to avoid per-call heap allocation
        float[] rented = ArrayPool<float>.Shared.Rent(outCount);
        try
        {
            for (int i = 0; i < outCount; i++)
            {
                float srcIdx = i * 44100f / 16000f;
                int   lo     = (int)srcIdx;
                int   hi     = Math.Min(lo + 1, monoCount - 1);
                float frac   = srcIdx - lo;

                float monoLo = (samples[lo * 2] + samples[lo * 2 + 1]) * 0.5f;
                float monoHi = (samples[hi * 2] + samples[hi * 2 + 1]) * 0.5f;
                rented[i]    = monoLo + frac * (monoHi - monoLo);
            }

            // Copy into a fresh Memory<float> owned by this message
            var payload = new float[outCount];
            rented.AsSpan(0, outCount).CopyTo(payload);
            _audioChannel.Writer.TryWrite(payload.AsMemory());
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    // ─── Processing loop (dedicated background thread) ────────────────────────

    private async Task ProcessAudioLoop(CancellationToken token)
    {
        try
        {
            await foreach (var chunk in _audioChannel!.Reader.ReadAllAsync(token))
            {
                // Append incoming samples into the ring buffer
                var span = chunk.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    _ringBuffer[_ringHead] = span[i];
                    _ringHead = (_ringHead + 1) % _ringBuffer.Length;
                    if (_ringCount < _ringBuffer.Length) _ringCount++;
                }

                // Once we have a full window, process and slide forward
                while (_ringCount >= ChunkWindowSamples)
                {
                    // Extract the window in order
                    int windowStart = (_ringHead - _ringCount + _ringBuffer.Length) % _ringBuffer.Length;
                    float[] window = new float[ChunkWindowSamples];
                    for (int i = 0; i < ChunkWindowSamples; i++)
                        window[i] = _ringBuffer[(windowStart + i) % _ringBuffer.Length];

                    // Skip silent windows to avoid wasting Whisper time
                    if (!IsSilent(window))
                        await ProcessBuffer(window, token);

                    // Advance ring buffer by the step size (slide forward)
                    _ringCount -= ChunkStepSamples;
                }
            }

            // Flush remaining audio
            if (_ringCount > 0)
            {
                int windowStart = (_ringHead - _ringCount + _ringBuffer.Length) % _ringBuffer.Length;
                float[] tail = new float[_ringCount];
                for (int i = 0; i < _ringCount; i++)
                    tail[i] = _ringBuffer[(windowStart + i) % _ringBuffer.Length];

                if (!IsSilent(tail))
                    await ProcessBuffer(tail, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool IsSilent(float[] samples)
    {
        double sumSq = 0;
        foreach (float s in samples) sumSq += s * s;
        float rms = (float)Math.Sqrt(sumSq / samples.Length);
        return rms < SilenceThreshold;
    }

    // ─── Whisper inference ────────────────────────────────────────────────────

    private async Task ProcessBuffer(float[] audioData, CancellationToken token)
    {
        if (_processor == null) return;

        // Build a valid WAV stream so Whisper.net can parse sample rate / format.
        const short Channels      = 1;
        const short BitsPerSample = 16;
        int dataBytes = audioData.Length * 2;

        // Pre-size the buffer: 44-byte WAV header + PCM data
        using var ms = new MemoryStream(44 + dataBytes);
        using var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + dataBytes);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16);
        bw.Write((short)1);           // PCM
        bw.Write(Channels);
        bw.Write(SampleRate);
        bw.Write(SampleRate * Channels * BitsPerSample / 8);
        bw.Write((short)(Channels * BitsPerSample / 8));
        bw.Write(BitsPerSample);
        bw.Write("data"u8.ToArray()); bw.Write(dataBytes);

        for (int i = 0; i < audioData.Length; i++)
            bw.Write((short)Math.Clamp((int)(audioData[i] * 32767f), short.MinValue, short.MaxValue));

        bw.Flush();
        ms.Position = 0;

        await foreach (var result in _processor.ProcessAsync(ms, token))
        {
            var text = result.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var segment = new TranscriptionSegment(
                _currentTimeOffset + result.Start,
                _currentTimeOffset + result.End,
                text);

            _fullTranscript.Add(segment);
            SegmentTranscribed?.Invoke(this, new TranscriptionSegmentEventArgs(segment));
        }

        _currentTimeOffset += TimeSpan.FromSeconds((double)ChunkStepSamples / SampleRate);
    }

    // ─── Misc ─────────────────────────────────────────────────────────────────

    public List<TranscriptionSegment> GetFullTranscript() => new(_fullTranscript);

    public void Dispose()
    {
        StopTranscription();
        _whisperFactory?.Dispose();
    }
}
