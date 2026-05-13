using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public class WasapiRecorder : IDisposable
{
    private WasapiLoopbackCapture? _loopbackCapture;
    private WasapiCapture? _micCapture;
    private WaveFileWriter? _waveWriter;
    private LameMP3FileWriter? _mp3Writer;
    private OutputFormat _outputFormat;
    private string? _currentFilePath;
    
    private BufferedWaveProvider? _loopbackBuffer;
    private BufferedWaveProvider? _micBuffer;
    private MixingSampleProvider? _mixer;
    
    private bool _isRecording;
    private Task? _recordingTask;
    private CancellationTokenSource? _cts;

    public bool IsRecording => _isRecording;

    public void Start(string filePath, OutputFormat format = OutputFormat.Mp3)
    {
        if (_isRecording) return;

        _outputFormat = format;
        _currentFilePath = filePath;
        var directory = Path.GetDirectoryName(filePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Common format for mixing: 44.1kHz, Stereo, 32-bit float
        var mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        _loopbackCapture = new WasapiLoopbackCapture();
        _micCapture = new WasapiCapture();

        _loopbackBuffer = new BufferedWaveProvider(mixFormat) { DiscardOnBufferOverflow = true };
        _micBuffer = new BufferedWaveProvider(mixFormat) { DiscardOnBufferOverflow = true };

        _loopbackCapture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded > 0)
            {
                var resampled = Resample(e.Buffer, e.BytesRecorded, _loopbackCapture.WaveFormat, mixFormat);
                _loopbackBuffer.AddSamples(resampled, 0, resampled.Length);
            }
        };

        _micCapture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded > 0)
            {
                var resampled = Resample(e.Buffer, e.BytesRecorded, _micCapture.WaveFormat, mixFormat);
                _micBuffer.AddSamples(resampled, 0, resampled.Length);
            }
        };

        _mixer = new MixingSampleProvider(new[] { _loopbackBuffer.ToSampleProvider(), _micBuffer.ToSampleProvider() });

        var pcmFormat = new WaveFormat(44100, 16, 2);
        if (_outputFormat == OutputFormat.Mp3)
        {
            // LameMP3FileWriter needs PCM 16-bit input; we convert from float in RecordLoop
            _mp3Writer = new LameMP3FileWriter(_currentFilePath, pcmFormat, LAMEPreset.STANDARD);
        }
        else
        {
            _waveWriter = new WaveFileWriter(_currentFilePath, mixFormat);
        }

        _isRecording = true;
        _cts = new CancellationTokenSource();
        
        _loopbackCapture.StartRecording();
        _micCapture.StartRecording();

        _recordingTask = Task.Run(() => RecordLoop(_cts.Token));
    }

    private byte[] Resample(byte[] buffer, int length, WaveFormat inputFormat, WaveFormat outputFormat)
    {
        if (inputFormat.Equals(outputFormat))
        {
            byte[] result = new byte[length];
            Array.Copy(buffer, result, length);
            return result;
        }

        using var ms = new MemoryStream(buffer, 0, length);
        using var reader = new RawSourceWaveStream(ms, inputFormat);
        using var resampler = new MediaFoundationResampler(reader, outputFormat);
        
        byte[] outBuffer = new byte[length * 4]; // Estimate
        int read = resampler.Read(outBuffer, 0, outBuffer.Length);
        
        byte[] final = new byte[read];
        Array.Copy(outBuffer, final, read);
        return final;
    }

    private void RecordLoop(CancellationToken token)
    {
        // 20ms chunks at 44100Hz stereo = 44100 * 2 * 0.02 = 1764 samples
        const int ChunkMs = 20;
        const int SampleRate = 44100;
        const int Channels = 2;
        int chunkSamples = SampleRate * Channels * ChunkMs / 1000;
        float[] buffer = new float[chunkSamples];
        // PCM16 byte buffer used for MP3 encoding
        byte[] pcmBuffer = new byte[chunkSamples * 2];

        while (!token.IsCancellationRequested && _isRecording)
        {
            long loopStart = Environment.TickCount64;

            if ((_loopbackBuffer?.BufferedBytes ?? 0) == 0 && (_micBuffer?.BufferedBytes ?? 0) == 0)
            {
                Thread.Sleep(ChunkMs);
                continue;
            }

            int samplesRead = _mixer!.Read(buffer, 0, buffer.Length);
            if (samplesRead > 0)
            {
                if (_outputFormat == OutputFormat.Mp3)
                {
                    // Convert float samples to 16-bit PCM
                    for (int i = 0; i < samplesRead; i++)
                    {
                        short s = (short)Math.Clamp((int)(buffer[i] * 32767f), short.MinValue, short.MaxValue);
                        pcmBuffer[i * 2] = (byte)(s & 0xFF);
                        pcmBuffer[i * 2 + 1] = (byte)(s >> 8);
                    }
                    _mp3Writer!.Write(pcmBuffer, 0, samplesRead * 2);
                }
                else
                {
                    _waveWriter!.WriteSamples(buffer, 0, samplesRead);
                }
            }

            int elapsedMs = (int)(Environment.TickCount64 - loopStart);
            int sleepMs = ChunkMs - elapsedMs;
            if (sleepMs > 0)
                Thread.Sleep(sleepMs);
        }
    }

    public void Stop()
    {
        if (!_isRecording) return;

        _isRecording = false;
        _cts?.Cancel();
        _recordingTask?.Wait();

        _loopbackCapture?.StopRecording();
        _micCapture?.StopRecording();

        _waveWriter?.Dispose();
        _waveWriter = null;
        _mp3Writer?.Dispose();
        _mp3Writer = null;

        _loopbackCapture?.Dispose();
        _micCapture?.Dispose();
        
        _cts?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}
