using System;
using System.Diagnostics;
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

public class WasapiRecorder : IAudioRecorder
{
    private readonly AppSettings? _settings;
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

    private DateTime _recordingStartTime;
    private DateTime _lastMicAudioTime;
    private bool _hasReportedMicSilence;

    private readonly object _micLock = new();
    private WaveFormat? _mixFormat;

    public bool IsRecording => _isRecording;

    public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;
    public event EventHandler<string>? MicrophoneWarning;
    public event EventHandler? MicrophoneRestored;

    public WasapiRecorder(AppSettings? settings = null)
    {
        _settings = settings;
    }

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
        _mixFormat = mixFormat;

        _loopbackCapture = new WasapiLoopbackCapture();
        lock (_micLock)
        {
            _micCapture = CreateMicCapture(_settings?.MicrophoneDeviceId);
        }

        Debug.WriteLine($"[WasapiRecorder] Loopback format: {_loopbackCapture.WaveFormat.SampleRate}Hz, {_loopbackCapture.WaveFormat.Channels}ch, {_loopbackCapture.WaveFormat.BitsPerSample}bit");
        Debug.WriteLine($"[WasapiRecorder] Mic format: {_micCapture.WaveFormat.SampleRate}Hz, {_micCapture.WaveFormat.Channels}ch, {_micCapture.WaveFormat.BitsPerSample}bit");

        _loopbackBuffer = new BufferedWaveProvider(mixFormat) { DiscardOnBufferOverflow = true };
        _micBuffer = new BufferedWaveProvider(mixFormat) { DiscardOnBufferOverflow = true };

        _loopbackCapture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded > 0)
            {
                var resampled = Resample(e.Buffer, e.BytesRecorded, _loopbackCapture.WaveFormat, mixFormat);
                if (resampled.Length > 0)
                {
                    _loopbackBuffer.AddSamples(resampled, 0, resampled.Length);
                }
            }
        };

        AttachMicCaptureHandlers(_micCapture, mixFormat);

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
        _recordingStartTime = DateTime.UtcNow;
        _lastMicAudioTime = DateTime.UtcNow;
        _hasReportedMicSilence = false;
        _cts = new CancellationTokenSource();
        
        _loopbackCapture.StartRecording();
        lock (_micLock)
        {
            _micCapture.StartRecording();
        }

        _recordingTask = Task.Run(() => RecordLoop(_cts.Token));
    }

    private void AttachMicCaptureHandlers(WasapiCapture capture, WaveFormat mixFormat)
    {
        capture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded > 0)
            {
                float peak = CalculatePeak(e.Buffer, e.BytesRecorded, capture.WaveFormat);
                if (peak > 0.001f) // above -60dB noise threshold
                {
                    _lastMicAudioTime = DateTime.UtcNow;
                    if (_hasReportedMicSilence)
                    {
                        _hasReportedMicSilence = false;
                        MicrophoneRestored?.Invoke(this, EventArgs.Empty);
                    }
                }

                var resampled = Resample(e.Buffer, e.BytesRecorded, capture.WaveFormat, mixFormat);
                if (resampled.Length > 0)
                {
                    _micBuffer?.AddSamples(resampled, 0, resampled.Length);
                }
            }
        };

        capture.RecordingStopped += (s, e) =>
        {
            if (e.Exception != null && _isRecording)
            {
                Debug.WriteLine($"[WasapiRecorder] Mic capture stopped with exception: {e.Exception.Message}");
                MicrophoneWarning?.Invoke(this, Resources.MicrophoneDisconnectedWarning);
                Task.Run(() => SwitchMicrophone(""));
            }
        };
    }

    public bool SwitchMicrophone(string? deviceId)
    {
        if (_settings != null && deviceId != null)
        {
            _settings.MicrophoneDeviceId = deviceId;
        }

        if (!_isRecording || _mixFormat == null)
        {
            return true;
        }

        lock (_micLock)
        {
            try
            {
                var oldMic = _micCapture;
                var newMic = CreateMicCapture(deviceId);

                AttachMicCaptureHandlers(newMic, _mixFormat);
                newMic.StartRecording();

                _micCapture = newMic;

                if (oldMic != null)
                {
                    try
                    {
                        oldMic.StopRecording();
                        oldMic.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WasapiRecorder] Error stopping/disposing previous mic: {ex.Message}");
                    }
                }

                _lastMicAudioTime = DateTime.UtcNow;
                _hasReportedMicSilence = false;
                Debug.WriteLine($"[WasapiRecorder] Switched microphone to: {deviceId}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WasapiRecorder] Failed to switch microphone: {ex.Message}");
                return false;
            }
        }
    }

    private WasapiCapture CreateMicCapture(string? targetDeviceId = null)
    {
        targetDeviceId ??= _settings?.MicrophoneDeviceId;

        try
        {
            using var enumerator = new MMDeviceEnumerator();

            if (!string.IsNullOrWhiteSpace(targetDeviceId))
            {
                try
                {
                    var device = enumerator.GetDevice(targetDeviceId);
                    if (device != null && device.State == DeviceState.Active)
                    {
                        Debug.WriteLine($"[WasapiRecorder] Using configured microphone: {device.FriendlyName} ({device.ID})");
                        return new WasapiCapture(device);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WasapiRecorder] Failed to open configured mic {targetDeviceId}: {ex.Message}. Falling back to default.");
                }
            }

            foreach (var role in new[] { Role.Communications, Role.Console })
            {
                try
                {
                    var defaultMic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                    if (defaultMic != null)
                    {
                        Debug.WriteLine($"[WasapiRecorder] Using default {role} microphone: {defaultMic.FriendlyName}");
                        return new WasapiCapture(defaultMic);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WasapiRecorder] Failed to query default {role} endpoint: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WasapiRecorder] Error enumerating audio devices: {ex.Message}");
        }

        Debug.WriteLine("[WasapiRecorder] Falling back to default WasapiCapture()");
        return new WasapiCapture();
    }

    private byte[] Resample(byte[] buffer, int length, WaveFormat inputFormat, WaveFormat outputFormat)
    {
        if (inputFormat.Equals(outputFormat))
        {
            byte[] result = new byte[length];
            Array.Copy(buffer, result, length);
            return result;
        }

        try
        {
            using var ms = new MemoryStream(buffer, 0, length);
            using var reader = new RawSourceWaveStream(ms, inputFormat);
            using var resampler = new MediaFoundationResampler(reader, outputFormat);
            
            byte[] outBuffer = new byte[length * 4]; // Estimate
            int read = resampler.Read(outBuffer, 0, outBuffer.Length);
            
            byte[] final = new byte[read];
            Array.Copy(outBuffer, final, read);
            return final;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WasapiRecorder] Resampling failed for format {inputFormat} -> {outputFormat}: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    internal static float CalculatePeak(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded <= 0 || buffer == null) return 0f;

        float max = 0f;
        try
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                int sampleCount = Math.Min(bytesRecorded / 4, buffer.Length / 4);
                for (int i = 0; i < sampleCount; i++)
                {
                    float sample = Math.Abs(BitConverter.ToSingle(buffer, i * 4));
                    if (sample > max) max = sample;
                }
            }
            else if (format.BitsPerSample == 16)
            {
                int sampleCount = Math.Min(bytesRecorded / 2, buffer.Length / 2);
                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = BitConverter.ToInt16(buffer, i * 2);
                    float norm = Math.Abs(sample / 32768f);
                    if (norm > max) max = norm;
                }
            }
            else if (format.BitsPerSample == 24)
            {
                int sampleCount = Math.Min(bytesRecorded / 3, buffer.Length / 3);
                for (int i = 0; i < sampleCount; i++)
                {
                    int sample24 = (buffer[i * 3 + 0]) | (buffer[i * 3 + 1] << 8) | ((sbyte)buffer[i * 3 + 2] << 16);
                    float norm = Math.Abs(sample24 / 8388608f);
                    if (norm > max) max = norm;
                }
            }
            else if (format.BitsPerSample == 32)
            {
                int sampleCount = Math.Min(bytesRecorded / 4, buffer.Length / 4);
                for (int i = 0; i < sampleCount; i++)
                {
                    int sample32 = BitConverter.ToInt32(buffer, i * 4);
                    float norm = Math.Abs(sample32 / 2147483648f);
                    if (norm > max) max = norm;
                }
            }
        }
        catch
        {
            // Ignore format parsing exceptions and return default
        }
        return max;
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
        long lastSilenceCheckTick = Environment.TickCount64;

        while (!token.IsCancellationRequested && _isRecording)
        {
            long loopStart = Environment.TickCount64;

            // Check for prolonged mic silence every ~1000ms
            if (loopStart - lastSilenceCheckTick >= 1000)
            {
                lastSilenceCheckTick = loopStart;
                if ((DateTime.UtcNow - _recordingStartTime).TotalSeconds >= 5)
                {
                    if ((DateTime.UtcNow - _lastMicAudioTime).TotalSeconds >= 5 && !_hasReportedMicSilence)
                    {
                        _hasReportedMicSilence = true;
                        MicrophoneWarning?.Invoke(this, "Microphone silent / no audio detected");
                    }
                }
            }

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

                // Fire event for transcription service
                AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(buffer, samplesRead));
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
        lock (_micLock)
        {
            _micCapture?.StopRecording();
        }

        _waveWriter?.Dispose();
        _waveWriter = null;
        _mp3Writer?.Dispose();
        _mp3Writer = null;

        _loopbackCapture?.Dispose();
        lock (_micLock)
        {
            _micCapture?.Dispose();
            _micCapture = null;
        }
        
        _cts?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}
