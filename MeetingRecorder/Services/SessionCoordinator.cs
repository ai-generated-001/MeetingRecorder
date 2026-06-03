using System;
using System.IO;
using System.Linq;
using System.Text;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public sealed class SessionCoordinator : IDisposable
{
    private readonly IAudioSessionMonitor _audioSessionMonitor;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly AppSettings _settings;
    private readonly IFileIOService _fileIOService;
    private readonly INoteWriterService _noteWriterService;
    private readonly ICloudSyncService _cloudSyncService;

    private string? _currentAudioPath;
    private string? _currentNotesPath;
    private DateTime _recordingStartTime;
    private MeetingDetectedEventArgs? _currentMeeting;

    public SessionState State { get; private set; } = SessionState.Idle;

    public event EventHandler<RecordingRequestedEventArgs>? RecordingRequested;
    public event EventHandler? RecordingStopped;
    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public SessionCoordinator(
        IAudioSessionMonitor audioSessionMonitor,
        IDateTimeProvider dateTimeProvider,
        TimeSpan debounceDuration,
        AppSettings settings,
        IFileIOService fileIOService,
        INoteWriterService noteWriterService,
        ICloudSyncService cloudSyncService)
    {
        _audioSessionMonitor = audioSessionMonitor;
        _dateTimeProvider = dateTimeProvider;
        _settings = settings;
        _fileIOService = fileIOService;
        _noteWriterService = noteWriterService;
        _cloudSyncService = cloudSyncService;

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
        if (_audioSessionMonitor.IsMonitoring)
        {
            _audioSessionMonitor.StopMonitoring();
        }

        if (State == SessionState.Recording)
        {
            OnMeetingEnded(this, EventArgs.Empty);
        }
        else
        {
            TransitionTo(SessionState.Idle);
        }
    }

    public void StopRecordingManually()
    {
        if (State != SessionState.Recording)
        {
            return;
        }

        OnMeetingEnded(this, EventArgs.Empty);

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
        if (State == SessionState.Recording || State == SessionState.Saving)
        {
            return;
        }

        _currentMeeting = e;
        _recordingStartTime = _dateTimeProvider.Now;

        string ext = _settings.OutputFormat == OutputFormat.Mp3 ? "mp3" : "wav";
        string baseName = $"Meeting_{_recordingStartTime:yyyyMMdd_HHmmss}";
        string? titlePrefix = SanitizeFileNameSegment(e.WindowTitle);
        
        string fileName = string.IsNullOrWhiteSpace(titlePrefix)
            ? $"{baseName}.{ext}"
            : $"{titlePrefix}_{baseName}.{ext}";
        _currentAudioPath = Path.Combine(_settings.OutputDirectory, fileName);

        string notesFileName = string.IsNullOrWhiteSpace(titlePrefix)
            ? $"{baseName}.md"
            : $"{titlePrefix}_{baseName}.md";
        _currentNotesPath = Path.Combine(_settings.OutputDirectory, notesFileName);

        RecordingRequested?.Invoke(this, new RecordingRequestedEventArgs(e, _currentAudioPath));
        TransitionTo(SessionState.Recording);
    }

    private void OnMeetingEnded(object? sender, EventArgs e)
    {
        if (State != SessionState.Recording)
        {
            return;
        }

        TransitionTo(SessionState.Saving);

        RecordingStopped?.Invoke(this, EventArgs.Empty);

        if (_currentNotesPath != null && _currentMeeting != null)
        {
            try
            {
                var endTime = _dateTimeProvider.Now;
                var duration = endTime - _recordingStartTime;

                var builder = new StringBuilder();
                builder.AppendLine($"# Meeting Notes: {_currentMeeting.WindowTitle ?? _currentMeeting.ProcessName}");
                builder.AppendLine();
                builder.AppendLine("**Session Info:**");
                builder.AppendLine($"- **Date:** {_recordingStartTime:yyyy-MM-dd}");
                builder.AppendLine($"- **Start Time:** {_recordingStartTime:HH:mm:ss}");
                builder.AppendLine($"- **End Time:** {endTime:HH:mm:ss}");
                builder.AppendLine($"- **Duration:** {duration:hh\\:mm\\:ss}");
                if (_currentAudioPath != null)
                {
                    builder.AppendLine($"- **Audio File:** {Path.GetFileName(_currentAudioPath)}");
                }
                builder.AppendLine();
                builder.AppendLine("**Timeline:**");
                builder.AppendLine(_noteWriterService.BuildMarkdownNote(_recordingStartTime, _recordingStartTime, "Meeting recording started."));
                builder.AppendLine(_noteWriterService.BuildMarkdownNote(_recordingStartTime, endTime, "Meeting recording ended."));

                _fileIOService.EnsureDirectory(_settings.OutputDirectory);
                _fileIOService.AppendAllTextAsync(_currentNotesPath, builder.ToString()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write notes file: {ex}");
            }
        }

        if (_currentAudioPath != null)
        {
            _cloudSyncService.EnqueueUpload(_currentAudioPath);
        }
        if (_currentNotesPath != null)
        {
            _cloudSyncService.EnqueueUpload(_currentNotesPath);
        }

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

    private static string? SanitizeFileNameSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .Trim()
            .Select(c => invalidChars.Contains(c) ? '_' : c)
            .ToArray());

        cleaned = cleaned.Replace(' ', '_');
        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        return cleaned.Trim('_') switch
        {
            "" => null,
            var s when s.Length > 60 => s[..60],
            var s => s
        };
    }

    public void Dispose()
    {
        _audioSessionMonitor.MeetingStarted -= OnMeetingStarted;
        _audioSessionMonitor.MeetingEnded -= OnMeetingEnded;
    }
}
