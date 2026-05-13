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
    private bool _isMeetingActive;
    private DateTime? _lastActiveTime;
    private readonly CancellationTokenSource _cts = new();

    public event EventHandler? MeetingStarted;
    public event EventHandler? MeetingEnded;

    public AudioSessionDetector(AppSettings settings)
    {
        _settings = settings;
        _deviceEnumerator = new MMDeviceEnumerator();
        _whitelistedProcesses = new HashSet<string>(_settings.WhitelistedProcesses, StringComparer.OrdinalIgnoreCase);
    }

    public void StartMonitoring()
    {
        Task.Run(MonitorLoop, _cts.Token);
    }

    private async Task MonitorLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                CheckAudioSessions();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking audio sessions: {ex.Message}");
            }

            await Task.Delay(3000, _cts.Token);
        }
    }

    private void CheckAudioSessions()
    {
        bool anyWhitelistedActive = false;

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
                            anyWhitelistedActive = true;
                            session.Dispose();
                            break;
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

        if (anyWhitelistedActive)
        {
            _lastActiveTime = DateTime.UtcNow;
            if (!_isMeetingActive)
            {
                _isMeetingActive = true;
                MeetingStarted?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            if (_isMeetingActive)
            {
                if (_lastActiveTime.HasValue && (DateTime.UtcNow - _lastActiveTime.Value).TotalSeconds >= _settings.DebounceSeconds)
                {
                    _isMeetingActive = false;
                    MeetingEnded?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _deviceEnumerator.Dispose();
    }
}
