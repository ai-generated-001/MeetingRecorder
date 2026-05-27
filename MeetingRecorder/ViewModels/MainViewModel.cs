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

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAudioRecorder _recorder;
    private readonly SessionCoordinator _sessionCoordinator;
    private readonly IFileIOService _fileIOService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private AppStatus _status = AppStatus.Idle;
    private string _statusText = Resources.Idle;

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

    public string SettingsButtonText => Resources.Settings;
    public string ExitButtonText => Resources.Exit;
    public string AppTitle => Resources.AppTitle;
    public string HeaderTitle => Resources.HeaderTitle;
    public string AppDescription => Resources.AppDescription;
    public string StatusLabel => Resources.StatusLabel;
    public string OutputFormatLabel => Resources.OutputFormatLabel;
    public string StartMonitoringText => Resources.StartMonitoring;
    public string StopMonitoringText => Resources.StopMonitoring;
    public string StopRecordingText => Resources.StopRecording;
    public string OpenFolderText => Resources.OpenFolder;
    public string ShowStatusWindowText => Resources.ShowStatusWindow;

    public MainViewModel(
        AppSettings settings,
        IAudioRecorder recorder,
        SessionCoordinator sessionCoordinator,
        IFileIOService fileIOService,
        IDateTimeProvider dateTimeProvider)
    {
        _settings = settings;
        _recorder = recorder;
        _sessionCoordinator = sessionCoordinator;
        _fileIOService = fileIOService;
        _dateTimeProvider = dateTimeProvider;

        _sessionCoordinator.RecordingRequested += OnRecordingRequested;
        _sessionCoordinator.RecordingStopped += OnRecordingStopped;
        _sessionCoordinator.StateChanged += OnStateChanged;

        OpenFolderCommand = new RelayCommand(_ => OpenRecordingsFolder());
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        StartMonitoringCommand = new RelayCommand(_ => StartMonitoring(), _ => Status == AppStatus.Idle);
        StopMonitoringCommand = new RelayCommand(_ => StopMonitoring(), _ => Status != AppStatus.Idle);
        StopRecordingCommand = new RelayCommand(_ => _sessionCoordinator.StopRecordingManually(), _ => Status == AppStatus.Recording);
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
        _sessionCoordinator.Start();
    }

    private void StopMonitoring()
    {
        _sessionCoordinator.Stop();
    }

    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(_settings.OutputDirectory, _settings.UiLanguage)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (settingsWindow.ShowDialog() == true)
        {
            _settings.OutputDirectory = settingsWindow.OutputDirectory;
            _settings.UiLanguage = settingsWindow.UiLanguage;
            App.ApplyUiLanguage(_settings.UiLanguage);
            UpdateLanguage();
        }
    }

    public void UpdateLanguage()
    {
        OnPropertyChanged(nameof(SettingsButtonText));
        OnPropertyChanged(nameof(ExitButtonText));
        OnPropertyChanged(nameof(AppTitle));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(AppDescription));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(OutputFormatLabel));
        OnPropertyChanged(nameof(StartMonitoringText));
        OnPropertyChanged(nameof(StopMonitoringText));
        OnPropertyChanged(nameof(StopRecordingText));
        OnPropertyChanged(nameof(OpenFolderText));
        OnPropertyChanged(nameof(ShowStatusWindowText));
        UpdateStatusText();
    }

    private void OnRecordingRequested(object? sender, MeetingDetectedEventArgs e)
    {
        string ext = _settings.OutputFormat == OutputFormat.Mp3 ? "mp3" : "wav";
        string baseName = $"Meeting_{_dateTimeProvider.Now:yyyyMMdd_HHmmss}";
        string? titlePrefix = SanitizeFileNameSegment(e.WindowTitle);
        string fileName = string.IsNullOrWhiteSpace(titlePrefix)
            ? $"{baseName}.{ext}"
            : $"{titlePrefix}_{baseName}.{ext}";
        string filePath = Path.Combine(_settings.OutputDirectory, fileName);

        _recorder.Start(filePath, _settings.OutputFormat);
    }

    private void OnRecordingStopped(object? sender, EventArgs e)
    {
        _recorder.Stop();
    }

    private void OnStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        Status = e.NewState switch
        {
            SessionState.Idle => AppStatus.Idle,
            SessionState.Detecting => AppStatus.Detecting,
            SessionState.Recording => AppStatus.Recording,
            _ => AppStatus.Idle
        };
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
            AppStatus.Idle => Resources.Idle,
            AppStatus.Detecting => Resources.StatusDetecting,
            AppStatus.Recording => Resources.Recording,
            _ => Resources.StatusUnknown
        };
    }

    private void OpenRecordingsFolder()
    {
        _fileIOService.EnsureDirectory(_settings.OutputDirectory);
        Process.Start("explorer.exe", _settings.OutputDirectory);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _sessionCoordinator.RecordingRequested -= OnRecordingRequested;
        _sessionCoordinator.RecordingStopped -= OnRecordingStopped;
        _sessionCoordinator.StateChanged -= OnStateChanged;
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
