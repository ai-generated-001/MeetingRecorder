using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public class DashScopeTranscriptionService : ITranscriptionService
{
    private const int TargetSampleRate = 16000;
    private const int InputSampleRate = 44100;
    private const int FrameDurationMs = 40; // 40ms frame for low-latency streaming
    private const int FrameSamples = TargetSampleRate * FrameDurationMs / 1000; // 40ms = 640 samples
    private const int FrameBytes = FrameSamples * 2;       // 16-bit PCM = 1280 bytes

    private readonly AppSettings _settings;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _sendAudioTask;
    private Channel<byte[]>? _audioChannel;

    private readonly List<TranscriptionSegment> _fullTranscript = new();
    private TranscriptionSegment? _latestPartialSegment;
    private readonly object _transcriptLock = new();

    private readonly byte[] _pcmBuffer = new byte[FrameBytes * 8];
    private int _pcmBufferCount = 0;

    private string? _currentTaskId;
    private TimeSpan _sessionStartTimeOffset = TimeSpan.Zero;
    private DateTime _sessionStartTime = DateTime.UtcNow;

    public bool IsTranscribing { get; private set; }

    public event EventHandler<TranscriptionSegmentEventArgs>? SegmentTranscribed;
    public event EventHandler<TranscriptionSegmentEventArgs>? PartialSegmentTranscribed;
    public event EventHandler<string>? StatusChanged;

    public DashScopeTranscriptionService(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Connectivity test / pre-check
        string apiKey = _settings.DashScopeApiKey?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("DashScope API Key is not configured in Settings.");
        }

        var wsUri = GetWebSocketUri(_settings.DashScopeBaseUrl);
        using var testWs = new ClientWebSocket();
        testWs.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        
        await testWs.ConnectAsync(wsUri, linkedCts.Token);
        if (testWs.State == WebSocketState.Open)
        {
            await testWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test completed", linkedCts.Token);
        }
    }

    public Task DisconnectAsync()
    {
        StopTranscription();
        return Task.CompletedTask;
    }

    public void StartTranscription()
    {
        if (IsTranscribing) return;

        string apiKey = _settings.DashScopeApiKey?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusChanged?.Invoke(this, "Error: DashScope API Key is missing.");
            return;
        }

        lock (_transcriptLock)
        {
            _fullTranscript.Clear();
            _latestPartialSegment = null;
        }
        
        _pcmBufferCount = 0;

        _sessionStartTime = DateTime.UtcNow;
        _currentTaskId = Guid.NewGuid().ToString("N");
        _cts = new CancellationTokenSource();

        _audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(250)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        IsTranscribing = true;
        StatusChanged?.Invoke(this, "Connecting to DashScope...");

        Task.Run(async () =>
        {
            try
            {
                await RunStreamingSessionAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DashScope] Session error: {ex.Message}");
                StatusChanged?.Invoke(this, $"Transcription error: {ex.Message}");
            }
        });
    }

    public void StopTranscription()
    {
        if (!IsTranscribing) return;
        IsTranscribing = false;

        try
        {
            _audioChannel?.Writer.TryComplete();
            _cts?.Cancel();

            if (_webSocket != null && _webSocket.State == WebSocketState.Open && _currentTaskId != null)
            {
                // Send finish-task
                var finishMsg = new
                {
                    header = new
                    {
                        action = "finish-task",
                        task_id = _currentTaskId,
                        streaming = "duplex"
                    },
                    payload = new
                    {
                        input = new { }
                    }
                };
                var json = JsonSerializer.Serialize(finishMsg);
                var bytes = Encoding.UTF8.GetBytes(json);
                _ = _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DashScope] Error during StopTranscription: {ex.Message}");
        }
        finally
        {
            StatusChanged?.Invoke(this, "Transcription stopped.");
        }
    }

    public void FeedAudioData(float[] samples, int count)
    {
        if (!IsTranscribing || _audioChannel == null) return;

        // Convert stereo 44.1 kHz float samples to mono 16 kHz 16-bit PCM
        int monoCount = count / 2;
        int outCount = (int)(monoCount * 16000L / 44100L);
        if (outCount == 0) return;

        int requiredBytes = outCount * 2;
        byte[] rentedPcm = ArrayPool<byte>.Shared.Rent(requiredBytes);

        try
        {
            for (int i = 0; i < outCount; i++)
            {
                float srcIdx = i * 44100f / 16000f;
                int lo = (int)srcIdx;
                int hi = Math.Min(lo + 1, monoCount - 1);
                float frac = srcIdx - lo;

                float monoLo = (samples[lo * 2] + samples[lo * 2 + 1]) * 0.5f;
                float monoHi = (samples[hi * 2] + samples[hi * 2 + 1]) * 0.5f;
                float sample = monoLo + frac * (monoHi - monoLo);

                short pcmVal = (short)Math.Clamp((int)(sample * 32767f), short.MinValue, short.MaxValue);
                rentedPcm[i * 2] = (byte)(pcmVal & 0xFF);
                rentedPcm[i * 2 + 1] = (byte)((pcmVal >> 8) & 0xFF);
            }

            // Buffer and package into ~40ms frames (1280 bytes)
            int offset = 0;
            while (offset < requiredBytes)
            {
                int needed = FrameBytes - _pcmBufferCount;
                int toCopy = Math.Min(needed, requiredBytes - offset);
                Buffer.BlockCopy(rentedPcm, offset, _pcmBuffer, _pcmBufferCount, toCopy);
                _pcmBufferCount += toCopy;
                offset += toCopy;

                if (_pcmBufferCount >= FrameBytes)
                {
                    byte[] frame = new byte[FrameBytes];
                    Buffer.BlockCopy(_pcmBuffer, 0, frame, 0, FrameBytes);
                    _audioChannel.Writer.TryWrite(frame);
                    _pcmBufferCount = 0;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedPcm);
        }
    }

    private async Task RunStreamingSessionAsync(CancellationToken token)
    {
        var wsUri = GetWebSocketUri(_settings.DashScopeBaseUrl);
        string apiKey = _settings.DashScopeApiKey.Trim();

        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        await _webSocket.ConnectAsync(wsUri, token);
        StatusChanged?.Invoke(this, "Connected. Initializing stream...");

        // Build parameters with optional language hints and custom vocabulary/hotwords ID
        var parametersDict = new Dictionary<string, object>
        {
            ["format"] = "pcm",
            ["sample_rate"] = TargetSampleRate
        };

        var langHints = GetLanguageHints(_settings.TranscriptionLanguage);
        if (langHints != null)
        {
            parametersDict["language_hints"] = langHints;
        }

        string vocId = _settings.VocabularyId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(vocId))
        {
            parametersDict["vocabulary_id"] = vocId;
            parametersDict["phrase_id"] = vocId;
        }

        // Send run-task message
        var runTaskPayload = new
        {
            header = new
            {
                action = "run-task",
                task_id = _currentTaskId,
                streaming = "duplex"
            },
            payload = new
            {
                task_group = "audio",
                task = "asr",
                function = "recognition",
                model = "paraformer-realtime-v2",
                parameters = parametersDict,
                input = new { }
            }
        };

        string runTaskJson = JsonSerializer.Serialize(runTaskPayload);
        byte[] runTaskBytes = Encoding.UTF8.GetBytes(runTaskJson);
        await _webSocket.SendAsync(new ArraySegment<byte>(runTaskBytes), WebSocketMessageType.Text, true, token);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_webSocket, token), token);
        _sendAudioTask = Task.Run(() => SendAudioLoopAsync(_webSocket, token), token);

        await Task.WhenAll(_receiveTask, _sendAudioTask);
    }

    private static string[]? GetLanguageHints(string? lang) => lang?.ToLower() switch
    {
        "zh" => new[] { "zh" },
        "en" => new[] { "en" },
        "ja" => new[] { "ja" },
        "ko" => new[] { "ko" },
        "fr" => new[] { "fr" },
        "de" => new[] { "de" },
        "es" => new[] { "es" },
        _ => null
    };

    private async Task SendAudioLoopAsync(ClientWebSocket ws, CancellationToken token)
    {
        try
        {
            if (_audioChannel == null) return;
            await foreach (var frame in _audioChannel.Reader.ReadAllAsync(token))
            {
                if (ws.State != WebSocketState.Open) break;
                await ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DashScope] Audio sender loop error: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        using var ms = new MemoryStream();

        try
        {
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        StatusChanged?.Invoke(this, "DashScope connection closed.");
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    if (ms.TryGetBuffer(out ArraySegment<byte> segment))
                    {
                        ProcessServerMessage(segment.AsMemory(0, (int)ms.Length));
                    }
                    else
                    {
                        ProcessServerMessage(ms.ToArray());
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DashScope] Receive loop error: {ex.Message}");
            StatusChanged?.Invoke(this, $"Transcription receive error: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ProcessServerMessage(ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            using var doc = JsonDocument.Parse(utf8Json);
            var root = doc.RootElement;

            if (root.TryGetProperty("header", out var header))
            {
                string @event = header.TryGetProperty("event", out var evProp) ? evProp.GetString() ?? "" : "";
                
                if (@event == "task-started")
                {
                    StatusChanged?.Invoke(this, "Transcribing in real-time...");
                }
                else if (@event == "result-generated")
                {
                    if (root.TryGetProperty("payload", out var payload) &&
                        payload.TryGetProperty("output", out var output) &&
                        output.TryGetProperty("sentence", out var sentence))
                    {
                        string text = sentence.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
                        bool isEnd = sentence.TryGetProperty("sentence_end", out var endProp) && endProp.GetBoolean();
                        
                        long beginTimeMs = sentence.TryGetProperty("begin_time", out var bProp) ? bProp.GetInt64() : 0;
                        long endTimeMs = sentence.TryGetProperty("end_time", out var eProp) ? eProp.GetInt64() : 0;

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            TimeSpan start = TimeSpan.FromMilliseconds(beginTimeMs);
                            TimeSpan end = endTimeMs > beginTimeMs 
                                ? TimeSpan.FromMilliseconds(endTimeMs) 
                                : start + TimeSpan.FromSeconds(2);

                            var segment = new TranscriptionSegment(start, end, text.Trim());

                            if (isEnd)
                            {
                                lock (_transcriptLock)
                                {
                                    _fullTranscript.Add(segment);
                                    _latestPartialSegment = null;
                                }
                                SegmentTranscribed?.Invoke(this, new TranscriptionSegmentEventArgs(segment));
                            }
                            else
                            {
                                lock (_transcriptLock)
                                {
                                    _latestPartialSegment = segment;
                                }
                                PartialSegmentTranscribed?.Invoke(this, new TranscriptionSegmentEventArgs(segment));
                                StatusChanged?.Invoke(this, $"[Live] {text}");
                            }
                        }
                    }
                }
                else if (@event == "task-finished")
                {
                    StatusChanged?.Invoke(this, "Session finished.");
                }
                else if (@event == "task-failed")
                {
                    string errorMsg = header.TryGetProperty("error_message", out var errProp) ? errProp.GetString() ?? "Unknown error" : "Task failed";
                    StatusChanged?.Invoke(this, $"Error: {errorMsg}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DashScope] Failed to parse message: {ex.Message}");
        }
    }

    public static Uri GetWebSocketUri(string? baseUrl)
    {
        string baseStr = baseUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(baseStr))
        {
            baseStr = "https://dashscope.aliyuncs.com";
        }

        baseStr = baseStr.TrimEnd('/');

        // Strip HTTP REST paths like /compatible-mode/v1, /compatible-mode, /v1
        // so that the root host is used for WebSocket ASR connection
        if (baseStr.EndsWith("/compatible-mode/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = baseStr.Substring(0, baseStr.Length - "/compatible-mode/v1".Length);
        }
        else if (baseStr.EndsWith("/compatible-mode", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = baseStr.Substring(0, baseStr.Length - "/compatible-mode".Length);
        }
        else if (baseStr.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
                 !baseStr.EndsWith("/api-ws/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = baseStr.Substring(0, baseStr.Length - "/v1".Length);
        }

        baseStr = baseStr.TrimEnd('/');

        if (baseStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = "ws://" + baseStr.Substring(7);
        }
        else if (baseStr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = "wss://" + baseStr.Substring(8);
        }
        else if (!baseStr.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
                 !baseStr.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = "wss://" + baseStr;
        }

        if (!baseStr.EndsWith("/api-ws/v1/inference", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = baseStr + "/api-ws/v1/inference";
        }

        return new Uri(baseStr);
    }

    public List<TranscriptionSegment> GetFullTranscript()
    {
        lock (_transcriptLock)
        {
            var list = new List<TranscriptionSegment>(_fullTranscript);
            if (_latestPartialSegment != null && !list.Any(s => s.Text == _latestPartialSegment.Text))
            {
                list.Add(_latestPartialSegment);
            }
            return list;
        }
    }

    public void Dispose()
    {
        StopTranscription();
        _webSocket?.Dispose();
        _cts?.Dispose();
    }
}
