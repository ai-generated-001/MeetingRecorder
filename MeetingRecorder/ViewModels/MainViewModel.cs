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

    partial void OnStatusChanged(AppStatus value)
    {
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

    public void Dispose()
    {
        _sessionCoordinator.RecordingRequested -= OnRecordingRequested;
        _sessionCoordinator.RecordingStopped -= OnRecordingStopped;
        _sessionCoordinator.StateChanged -= OnStateChanged;
    }
}
