using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingRecorder.Models;
using MeetingRecorder.Services;

namespace MeetingRecorder.ViewModels;

public enum AppStatus
{
    Idle,
    Detecting,
    Recording
}

public enum StatusMessageCategory
{
    Idle,
    Detecting,
    Recording,
    Uploading,
    UploadSuccess,
    UploadError
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAudioRecorder _recorder;
    private readonly SessionCoordinator _sessionCoordinator;
    private readonly IFileIOService _fileIOService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICloudSyncService _cloudSyncService;
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private AppStatus _status = AppStatus.Idle;

    [ObservableProperty]
    private string _statusText = Resources.Idle;

    [ObservableProperty]
    private StatusMessageCategory _statusCategory = StatusMessageCategory.Idle;

    // Transient override: set by upload callbacks; cleared on the next state change.
    private string? _uploadStatusText;
    private StatusMessageCategory _uploadCategory;

    partial void OnStatusChanged(AppStatus value)
    {
        _uploadStatusText = null;   // clear transient upload message on state change
        UpdateStatusText();
        StartMonitoringCommand.NotifyCanExecuteChanged();
        StopMonitoringCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
    }

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
    public string UploadToDriveText => Resources.UploadToDrive;

    public MainViewModel(
        AppSettings settings,
        IAudioRecorder recorder,
        SessionCoordinator sessionCoordinator,
        IFileIOService fileIOService,
        IDateTimeProvider dateTimeProvider,
        ICloudSyncService cloudSyncService,
        IServiceProvider serviceProvider)
    {
        _settings = settings;
        _recorder = recorder;
        _sessionCoordinator = sessionCoordinator;
        _fileIOService = fileIOService;
        _dateTimeProvider = dateTimeProvider;
        _cloudSyncService = cloudSyncService;
        _serviceProvider = serviceProvider;

        _sessionCoordinator.RecordingRequested += OnRecordingRequested;
        _sessionCoordinator.RecordingStopped += OnRecordingStopped;
        _sessionCoordinator.StateChanged += OnStateChanged;
        _cloudSyncService.UploadFailed += OnUploadFailed;
        _cloudSyncService.UploadCompleted += OnUploadCompleted;

        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            Status = AppStatus.Idle;
            return;
        }

        StartMonitoring();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        _fileIOService.EnsureDirectory(_settings.OutputDirectory);
        Process.Start("explorer.exe", _settings.OutputDirectory);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        settingsWindow.Owner = System.Windows.Application.Current?.MainWindow;

        if (settingsWindow.ShowDialog() == true)
        {
            UpdateLanguage();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartMonitoring))]
    private void StartMonitoring()
    {
        _sessionCoordinator.Start();
    }

    private bool CanStartMonitoring() => Status == AppStatus.Idle;

    [RelayCommand(CanExecute = nameof(CanStopMonitoring))]
    private void StopMonitoring()
    {
        _sessionCoordinator.Stop();
    }

    private bool CanStopMonitoring() => Status != AppStatus.Idle;

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private void StopRecording()
    {
        _sessionCoordinator.StopRecordingManually();
    }

    private bool CanStopRecording() => Status == AppStatus.Recording;

    [RelayCommand]
    private void Exit()
    {
        System.Windows.Application.Current.Shutdown();
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
        OnPropertyChanged(nameof(UploadToDriveText));
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

    private void ExecuteOnUIThread(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private void OnStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        ExecuteOnUIThread(() =>
        {
            Status = e.NewState switch
            {
                SessionState.Idle => AppStatus.Idle,
                SessionState.Detecting => AppStatus.Detecting,
                SessionState.Recording => AppStatus.Recording,
                SessionState.Saving => AppStatus.Detecting,
                _ => AppStatus.Idle
            };
        });
    }

    [RelayCommand]
    private void ManualUpload()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Resources.UploadToDrive,
            InitialDirectory = Directory.Exists(_settings.OutputDirectory)
                ? _settings.OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = "Audio Files (*.mp3;*.wav)|*.mp3;*.wav|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            var filePath = dialog.FileName;
            _uploadCategory = StatusMessageCategory.Uploading;
            _uploadStatusText = string.Format(Resources.UploadingFile, Path.GetFileName(filePath));
            UpdateStatusText();
            _cloudSyncService.EnqueueUpload(filePath);
        }
    }

    /// <summary>
    /// Called on the thread-pool when an upload permanently fails after all retries.
    /// Marshals to the UI thread and shows a transient error in the status area.
    /// </summary>
    private void OnUploadFailed(object? sender, string errorMessage)
    {
        ExecuteOnUIThread(() =>
        {
            _uploadCategory = StatusMessageCategory.UploadError;
            _uploadStatusText = string.Format(Resources.UploadFailed, errorMessage);
            UpdateStatusText();
        });
    }

    /// <summary>
    /// Called on the thread-pool when an upload completes successfully.
    /// Marshals to the UI thread and shows a transient success message.
    /// </summary>
    private void OnUploadCompleted(object? sender, string filePath)
    {
        ExecuteOnUIThread(() =>
        {
            _uploadCategory = StatusMessageCategory.UploadSuccess;
            _uploadStatusText = string.Format(Resources.UploadSucceeded, Path.GetFileName(filePath));
            UpdateStatusText();
        });
    }

    private void UpdateStatusText()
    {
        if (_uploadStatusText is not null && Status != AppStatus.Recording)
        {
            StatusText = _uploadStatusText;
            StatusCategory = _uploadCategory;
            return;
        }

        StatusText = Status switch
        {
            AppStatus.Idle => Resources.Idle,
            AppStatus.Detecting => Resources.StatusDetecting,
            AppStatus.Recording => Resources.Recording,
            _ => Resources.StatusUnknown
        };

        StatusCategory = Status switch
        {
            AppStatus.Idle => StatusMessageCategory.Idle,
            AppStatus.Detecting => StatusMessageCategory.Detecting,
            AppStatus.Recording => StatusMessageCategory.Recording,
            _ => StatusMessageCategory.Idle
        };
    }

    public void Dispose()
    {
        _sessionCoordinator.RecordingRequested -= OnRecordingRequested;
        _sessionCoordinator.RecordingStopped -= OnRecordingStopped;
        _sessionCoordinator.StateChanged -= OnStateChanged;
        _cloudSyncService.UploadFailed -= OnUploadFailed;
        _cloudSyncService.UploadCompleted -= OnUploadCompleted;
    }
}
