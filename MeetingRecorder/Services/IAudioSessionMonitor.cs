using System;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public interface IAudioSessionMonitor : IDisposable
{
    bool IsMonitoring { get; }
    event EventHandler<MeetingDetectedEventArgs>? MeetingStarted;
    event EventHandler? MeetingEnded;

    void StartMonitoring();
    void StopMonitoring();
    MeetingDetectedEventArgs? GetCurrentActiveMeeting();
}
