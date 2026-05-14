using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public class AudioSessionDetector : IDisposable
{
    private readonly AppSettings _settings;
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly HashSet<string> _whitelistedProcesses;
    private readonly object _sync = new();
    private bool _isMeetingActive;
    private DateTime? _lastActiveTime;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;
    private MeetingDetectedEventArgs? _currentMeeting;

    public bool IsMonitoring { get; private set; }
    public event EventHandler<MeetingDetectedEventArgs>? MeetingStarted;
    public event EventHandler? MeetingEnded;

    public AudioSessionDetector(AppSettings settings)
    {
        _settings = settings;
        _deviceEnumerator = new MMDeviceEnumerator();
        _whitelistedProcesses = new HashSet<string>(_settings.WhitelistedProcesses, StringComparer.OrdinalIgnoreCase);
    }

    public void StartMonitoring()
    {
        lock (_sync)
        {
            if (IsMonitoring)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitorLoop(_cts.Token), _cts.Token);
            IsMonitoring = true;
        }
    }

    public void StopMonitoring()
    {
        Task? monitoringTask;
        lock (_sync)
        {
            if (!IsMonitoring)
            {
                return;
            }

            _cts?.Cancel();
            monitoringTask = _monitoringTask;
        }

        try
        {
            monitoringTask?.Wait();
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException or OperationCanceledException))
        {
        }
        finally
        {
            lock (_sync)
            {
                _cts?.Dispose();
                _cts = null;
                _monitoringTask = null;
                IsMonitoring = false;
                _isMeetingActive = false;
                _lastActiveTime = null;
                _currentMeeting = null;
            }
        }
    }

    private async Task MonitorLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                CheckAudioSessions();
                await Task.Delay(3000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking audio sessions: {ex.Message}");
            }
        }
    }

    public MeetingDetectedEventArgs? GetCurrentActiveMeeting()
    {
        lock (_sync)
        {
            return FindActiveMeeting();
        }
    }

    private void CheckAudioSessions()
    {
        lock (_sync)
        {
            var detectedMeeting = FindActiveMeeting();
            bool anyWhitelistedActive = detectedMeeting is not null;

            if (anyWhitelistedActive)
            {
                _lastActiveTime = DateTime.UtcNow;
                if (!_isMeetingActive)
                {
                    _isMeetingActive = true;
                    _currentMeeting = detectedMeeting;
                    MeetingStarted?.Invoke(this, _currentMeeting ?? new MeetingDetectedEventArgs("Meeting", null));
                }
            }
            else
            {
                if (_isMeetingActive)
                {
                    if (_lastActiveTime.HasValue && (DateTime.UtcNow - _lastActiveTime.Value).TotalSeconds >= _settings.DebounceSeconds)
                    {
                        _isMeetingActive = false;
                        _currentMeeting = null;
                        MeetingEnded?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }
    }

    private MeetingDetectedEventArgs? FindActiveMeeting()
    {
        using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
        var sessionManager = device.AudioSessionManager;
        var sessions = sessionManager.Sessions;

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (session.State == AudioSessionState.AudioSessionStateActive)
            {
                uint processId = session.GetProcessID;
                if (processId != 0)
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)processId);
                        string processName = process.ProcessName;

                        if (_whitelistedProcesses.Contains(processName))
                        {
                            var result = new MeetingDetectedEventArgs(processName, process.MainWindowTitle);
                            session.Dispose();
                            return result;
                        }
                    }
                    catch
                    {
                        // Process might have exited
                    }
                }
            }

            session.Dispose();
        }

        // Note: sessions and sessionManager in NAudio 2.x don't implement IDisposable
        // but individual sessions do.
        return null;
    }

    public void Dispose()
    {
        StopMonitoring();
        _deviceEnumerator.Dispose();
    }
}
