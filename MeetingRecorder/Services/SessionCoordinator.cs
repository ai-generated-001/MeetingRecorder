using System;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public sealed class SessionCoordinator : IDisposable
{
    private readonly IAudioSessionMonitor _audioSessionMonitor;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly TimeSpan _debounceDuration;
    private DateTime? _meetingInactiveSinceUtc;

    public SessionState State { get; private set; } = SessionState.Idle;

    public event EventHandler<MeetingDetectedEventArgs>? RecordingRequested;
    public event EventHandler? RecordingStopped;
    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public SessionCoordinator(IAudioSessionMonitor audioSessionMonitor, IDateTimeProvider dateTimeProvider, TimeSpan debounceDuration)
    {
        _audioSessionMonitor = audioSessionMonitor;
        _dateTimeProvider = dateTimeProvider;
        _debounceDuration = debounceDuration;

        _audioSessionMonitor.MeetingStarted += OnMeetingStarted;
        _audioSessionMonitor.MeetingEnded += OnMeetingEnded;
    }

    public void Start()
    {
        if (_audioSessionMonitor.IsMonitoring)
        {
            return;
        }

        _audioSessionMonitor.StartMonitoring();
        TransitionTo(SessionState.Detecting);
    }

    public void Stop()
    {
        _meetingInactiveSinceUtc = null;

        if (_audioSessionMonitor.IsMonitoring)
        {
            _audioSessionMonitor.StopMonitoring();
        }

        if (State == SessionState.Recording)
        {
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        TransitionTo(SessionState.Idle);
    }

    public void StopRecordingManually()
    {
        if (State != SessionState.Recording)
        {
            return;
        }

        RecordingStopped?.Invoke(this, EventArgs.Empty);
        TransitionTo(_audioSessionMonitor.IsMonitoring ? SessionState.Detecting : SessionState.Idle);

        if (!_audioSessionMonitor.IsMonitoring)
        {
            return;
        }

        var currentMeeting = _audioSessionMonitor.GetCurrentActiveMeeting();
        if (currentMeeting is not null)
        {
            OnMeetingStarted(this, currentMeeting);
        }
    }

    private void OnMeetingStarted(object? sender, MeetingDetectedEventArgs e)
    {
        _meetingInactiveSinceUtc = null;

        if (State == SessionState.Recording)
        {
            return;
        }

        RecordingRequested?.Invoke(this, e);
        TransitionTo(SessionState.Recording);
    }

    private void OnMeetingEnded(object? sender, EventArgs e)
    {
        if (State != SessionState.Recording)
        {
            return;
        }

        var now = _dateTimeProvider.UtcNow;
        _meetingInactiveSinceUtc ??= now;

        if (now - _meetingInactiveSinceUtc.Value < _debounceDuration)
        {
            return;
        }

        _meetingInactiveSinceUtc = null;
        RecordingStopped?.Invoke(this, EventArgs.Empty);
        TransitionTo(_audioSessionMonitor.IsMonitoring ? SessionState.Detecting : SessionState.Idle);
    }

    private void TransitionTo(SessionState nextState)
    {
        if (State == nextState)
        {
            return;
        }

        var oldState = State;
        State = nextState;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(oldState, nextState));
    }

    public void Dispose()
    {
        _audioSessionMonitor.MeetingStarted -= OnMeetingStarted;
        _audioSessionMonitor.MeetingEnded -= OnMeetingEnded;
    }
}
