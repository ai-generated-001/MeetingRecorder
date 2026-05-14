using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MeetingRecorder.Models;
using MeetingRecorder.Services;

namespace MeetingRecorder.ViewModels;

public enum AppStatus
{
    Idle,
    Detecting,
    Recording
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private readonly AudioSessionDetector _detector;
    private readonly WasapiRecorder _recorder;
    private AppStatus _status = AppStatus.Idle;
    private string _statusText = "Initializing...";

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                UpdateStatusText();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand OpenFolderCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand StartMonitoringCommand { get; }
    public ICommand StopMonitoringCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand ExitCommand { get; }

    public OutputFormat OutputFormat
    {
        get => _settings.OutputFormat;
        set
        {
            if (_settings.OutputFormat != value)
            {
                _settings.OutputFormat = value;
                OnPropertyChanged();
            }
        }
    }

    public MainViewModel()
    {
        _settings = new AppSettings();
        _detector = new AudioSessionDetector(_settings);
        _recorder = new WasapiRecorder();

        _detector.MeetingStarted += OnMeetingStarted;
        _detector.MeetingEnded += OnMeetingEnded;

        OpenFolderCommand = new RelayCommand(_ => OpenRecordingsFolder());
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        StartMonitoringCommand = new RelayCommand(_ => StartMonitoring(), _ => !_detector.IsMonitoring);
        StopMonitoringCommand = new RelayCommand(_ => StopMonitoring(), _ => _detector.IsMonitoring);
        StopRecordingCommand = new RelayCommand(_ => StopRecordingAndRecheck(), _ => Status == AppStatus.Recording);
        ExitCommand = new RelayCommand(_ => System.Windows.Application.Current.Shutdown());

        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            Status = AppStatus.Idle;
            return;
        }

        StartMonitoring();
    }

    private void StartMonitoring()
    {
        if (_detector.IsMonitoring)
        {
            return;
        }

        _detector.StartMonitoring();
        Status = AppStatus.Detecting;
    }

    private void StopMonitoring()
    {
        if (!_detector.IsMonitoring)
        {
            return;
        }

        if (Status == AppStatus.Recording)
        {
            _recorder.Stop();
        }

        _detector.StopMonitoring();
        Status = AppStatus.Idle;
    }

    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(_settings.OutputDirectory)
        {
            Owner = Application.Current?.MainWindow
        };

        if (settingsWindow.ShowDialog() == true)
        {
            _settings.OutputDirectory = settingsWindow.OutputDirectory;
        }
    }

    private void OnMeetingStarted(object? sender, MeetingDetectedEventArgs e)
    {
        if (Status != AppStatus.Recording)
        {
            string ext = _settings.OutputFormat == OutputFormat.Mp3 ? "mp3" : "wav";
            string baseName = $"Meeting_{DateTime.Now:yyyyMMdd_HHmmss}";
            string? titlePrefix = SanitizeFileNameSegment(e.WindowTitle);
            string fileName = string.IsNullOrWhiteSpace(titlePrefix)
                ? $"{baseName}.{ext}"
                : $"{titlePrefix}_{baseName}.{ext}";
            string filePath = Path.Combine(_settings.OutputDirectory, fileName);

            _recorder.Start(filePath, _settings.OutputFormat);
            Status = AppStatus.Recording;
        }
    }

    private void OnMeetingEnded(object? sender, EventArgs e)
    {
        if (Status == AppStatus.Recording)
        {
            _recorder.Stop();
            Status = _detector.IsMonitoring ? AppStatus.Detecting : AppStatus.Idle;
        }
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

    private void UpdateStatusText()
    {
        StatusText = Status switch
        {
            AppStatus.Idle => "Monitoring stopped",
            AppStatus.Detecting => "Monitoring for meetings...",
            AppStatus.Recording => "Recording meeting...",
            _ => "Unknown"
        };
    }

    private void OpenRecordingsFolder()
    {
        if (!Directory.Exists(_settings.OutputDirectory))
        {
            Directory.CreateDirectory(_settings.OutputDirectory);
        }
        Process.Start("explorer.exe", _settings.OutputDirectory);
    }

    private void StopRecordingAndRecheck()
    {
        if (Status != AppStatus.Recording)
        {
            return;
        }

        _recorder.Stop();
        Status = _detector.IsMonitoring ? AppStatus.Detecting : AppStatus.Idle;

        if (!_detector.IsMonitoring)
        {
            return;
        }

        var activeMeeting = _detector.GetCurrentActiveMeeting();
        if (activeMeeting is not null)
        {
            OnMeetingStarted(this, activeMeeting);
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}
