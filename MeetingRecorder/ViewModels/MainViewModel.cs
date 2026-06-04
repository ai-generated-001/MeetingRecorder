using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private readonly ICloudSyncService _cloudSyncService;
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
                App.SaveSettings(_settings);
            }
        }
    }

    public string SettingsButtonText => Resources.Settings;
    public string ExitButtonText => Resources.Exit;
    public string AppTitle => string.Format("{0} {1}", Resources.AppTitle, Assembly.GetExecutingAssembly().GetName().Version);
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
        IDateTimeProvider dateTimeProvider,
        ICloudSyncService cloudSyncService)
    {
        _settings = settings;
        _recorder = recorder;
        _sessionCoordinator = sessionCoordinator;
        _fileIOService = fileIOService;
        _dateTimeProvider = dateTimeProvider;
        _cloudSyncService = cloudSyncService;

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
        var settingsWindow = new SettingsWindow(
            _settings.OutputDirectory,
            _settings.UiLanguage,
            _settings.GoogleDriveEnabled,
            _settings.GoogleClientId,
            _settings.GoogleClientSecret,
            _settings.GoogleDriveFolderPath,
            clearTokenAction: ClearGoogleToken,
            getLoginStatusFunc: async (ct) =>
            {
                if (_cloudSyncService is GoogleDriveSyncService syncService)
                {
                    return await syncService.GetAccountStatusStringAsync(ct);
                }
                return Resources.GoogleDriveNotSignedIn;
            },
            loginFunc: async (clientId, clientSecret, ct) =>
            {
                if (_cloudSyncService is GoogleDriveSyncService syncService)
                {
                    return await syncService.LoginAsync(clientId, clientSecret, ct);
                }
                return Resources.GoogleDriveNotSignedIn;
            })
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (settingsWindow.ShowDialog() == true)
        {
            _settings.OutputDirectory = settingsWindow.OutputDirectory;
            _settings.UiLanguage = settingsWindow.UiLanguage;

            // If the user changed the enabled flag, credentials, or target folder,
            // reset the cached drive service so the next upload re-authenticates / re-resolves.
            bool credentialsChanged =
                _settings.GoogleDriveEnabled != settingsWindow.GoogleDriveEnabled ||
                _settings.GoogleClientId != settingsWindow.GoogleClientId ||
                _settings.GoogleClientSecret != settingsWindow.GoogleClientSecret ||
                _settings.GoogleDriveFolderPath != settingsWindow.GoogleDriveFolderPath;

            _settings.GoogleDriveEnabled = settingsWindow.GoogleDriveEnabled;
            _settings.GoogleClientId = settingsWindow.GoogleClientId;
            _settings.GoogleClientSecret = settingsWindow.GoogleClientSecret;
            _settings.GoogleDriveFolderPath = settingsWindow.GoogleDriveFolderPath;

            if (credentialsChanged)
            {
                (_cloudSyncService as GoogleDriveSyncService)?.ResetCredentials();
            }

            App.ApplyUiLanguage(_settings.UiLanguage);
            UpdateLanguage();
            App.SaveSettings(_settings);
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

    private void OnRecordingRequested(object? sender, RecordingRequestedEventArgs e)
    {
        _recorder.Start(e.AudioFilePath, _settings.OutputFormat);
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
            SessionState.Saving => AppStatus.Idle,
            _ => AppStatus.Idle
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

    private void ClearGoogleToken()
    {
        // Wipe every encrypted token file in the token.json folder.
        var tokenDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "token.json");
        if (Directory.Exists(tokenDir))
        {
            foreach (var file in Directory.GetFiles(tokenDir, "dpapi_*.dat"))
            {
                try { File.Delete(file); }
                catch { /* best-effort */ }
            }
        }

        // Also reset the in-memory DriveService so the next upload re-authenticates.
        (_cloudSyncService as GoogleDriveSyncService)?.ResetCredentials();
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
