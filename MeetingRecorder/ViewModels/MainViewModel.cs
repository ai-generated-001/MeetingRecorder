using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
    private readonly ITranscriptionService _transcriptionService;
    private readonly IInsightService _insightService;
    private readonly TranscriptionOverlayViewModel _overlayViewModel;
    private readonly IPythonEnvSetupService? _pythonEnvSetupService;
    private TranscriptionOverlayWindow? _overlayWindow;

    private readonly List<TranscriptionSegment> _recentSegments = new();
    private readonly object _segmentsLock = new();
    private DateTime _lastMentionInsightTime = DateTime.MinValue;

    [ObservableProperty]
    private AppStatus _status = AppStatus.Idle;

    [ObservableProperty]
    private string _statusText = Resources.Idle;

    [ObservableProperty]
    private StatusMessageCategory _statusCategory = StatusMessageCategory.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOrganizeProgress))]
    private bool _isOrganizing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOrganizeProgress))]
    private string _organizeStatusText = "";

    [ObservableProperty]
    private int _organizeProgressValue;

    public bool ShowOrganizeProgress => IsOrganizing || !string.IsNullOrWhiteSpace(OrganizeStatusText);

    // Transient override: set by upload callbacks; cleared on the next state change.
    private string? _uploadStatusText;
    private StatusMessageCategory _uploadCategory;
    private string? _micWarningText;

    partial void OnStatusChanged(AppStatus value)
    {
        _uploadStatusText = null;   // clear transient upload message on state change
        _micWarningText = null;
        UpdateStatusText();
        StartMonitoringCommand.NotifyCanExecuteChanged();
        StopMonitoringCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ToggleMonitoringText));
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiToggleText))]
    [NotifyPropertyChangedFor(nameof(AiStatusText))]
    private bool _isAiEnabled;

    public string AiFeatureLabel => Resources.AiFeatureLabel;
    public string AiToggleText => IsAiEnabled ? Resources.TurnOffAi : Resources.TurnOnAi;
    public string AiStatusText => IsAiEnabled ? Resources.AiFeatureOn : Resources.AiFeatureOff;

    public string SettingsButtonText => Resources.Settings;
    public string ExitButtonText => Resources.Exit;
    public string AppTitle => string.Format("{0} {1}", Resources.AppTitle, Assembly.GetExecutingAssembly().GetName().Version);
    public string HeaderTitle => Resources.HeaderTitle;
    public string AppDescription => Resources.AppDescription;
    public string StatusLabel => Resources.StatusLabel;
    public string OutputFormatLabel => Resources.OutputFormatLabel;
    public string StartMonitoringText => Resources.StartMonitoring;
    public string StopMonitoringText => Resources.StopMonitoring;
    public string ToggleMonitoringText => Status == AppStatus.Idle ? Resources.StartMonitoring : Resources.StopMonitoring;
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
        IServiceProvider serviceProvider,
        ITranscriptionService transcriptionService,
        IInsightService insightService,
        TranscriptionOverlayViewModel overlayViewModel,
        IPythonEnvSetupService? pythonEnvSetupService = null)
    {
        _settings = settings;
        _recorder = recorder;
        _sessionCoordinator = sessionCoordinator;
        _fileIOService = fileIOService;
        _dateTimeProvider = dateTimeProvider;
        _cloudSyncService = cloudSyncService;
        _serviceProvider = serviceProvider;
        _transcriptionService = transcriptionService;
        _insightService = insightService;
        _overlayViewModel = overlayViewModel;
        _pythonEnvSetupService = pythonEnvSetupService ?? serviceProvider.GetService<IPythonEnvSetupService>();

        _isAiEnabled = _settings.TranscriptionEnabled;
        _overlayViewModel.IsAiActive = _isAiEnabled;
        _overlayViewModel.ToggleAiRequested += ToggleAi;

        _sessionCoordinator.RecordingRequested += OnRecordingRequested;
        _sessionCoordinator.RecordingStopped += OnRecordingStopped;
        _sessionCoordinator.StateChanged += OnStateChanged;
        _cloudSyncService.UploadFailed += OnUploadFailed;
        _cloudSyncService.UploadCompleted += OnUploadCompleted;
        _cloudSyncService.OrganizeProgressChanged += OnOrganizeProgressChanged;
        
        _recorder.AudioDataAvailable += OnAudioDataAvailable;
        _recorder.MicrophoneWarning += OnMicrophoneWarning;
        _recorder.MicrophoneRestored += OnMicrophoneRestored;
        _transcriptionService.SegmentTranscribed += OnSegmentTranscribed;

        if (_pythonEnvSetupService != null)
        {
            _pythonEnvSetupService.SetupCompleted += OnPythonEnvSetupCompleted;
        }

        IsOrganizing = _cloudSyncService.IsOrganizing;
        OrganizeStatusText = _cloudSyncService.OrganizeStatusText;
        OrganizeProgressValue = _cloudSyncService.OrganizeProgressValue;

        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            Status = AppStatus.Idle;
            return;
        }

        StartMonitoring();
    }
    
    private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
    {
        if (_settings.TranscriptionEnabled && _transcriptionService.IsTranscribing)
        {
            _transcriptionService.FeedAudioData(e.Buffer, e.Count);
        }
    }

    private void OnSegmentTranscribed(object? sender, TranscriptionSegmentEventArgs e)
    {
        lock (_segmentsLock)
        {
            _recentSegments.Add(e.Segment);
            if (_recentSegments.Count > 200)
            {
                _recentSegments.RemoveRange(0, _recentSegments.Count - 200);
            }
        }

        if (!_settings.InsightsEnabled || string.IsNullOrWhiteSpace(_settings.DashScopeApiKey))
        {
            return;
        }

        // Check if user is mentioned in this segment
        if (IsUserMentioned(e.Segment.Text, _settings.MentionNames))
        {
            // Debounce rapid consecutive mentions within 5 seconds
            var now = DateTime.UtcNow;
            if ((now - _lastMentionInsightTime).TotalSeconds < 5)
            {
                return;
            }
            _lastMentionInsightTime = now;

            string context = BuildRecentTranscriptContext(e.Segment.End, _settings.InsightContextSeconds);
            _ = _insightService.AnalyzeAsync(e.Segment.Text, context, _settings.TranscriptionLanguage);
        }
    }

    internal static bool IsUserMentioned(string text, IEnumerable<string>? mentionNames)
    {
        if (string.IsNullOrWhiteSpace(text) || mentionNames == null) return false;

        foreach (var nameEntry in mentionNames)
        {
            if (string.IsNullOrWhiteSpace(nameEntry)) continue;

            // Support alias syntax: "张伟|张维|Alex|Alec" or "张伟 / 张维"
            var aliases = nameEntry.Split(new[] { '|', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var alias in aliases)
            {
                string trimmed = alias.Trim();
                if (trimmed.Length == 0) continue;

                if (text.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string BuildRecentTranscriptContext(TimeSpan segmentEndTime, int contextSeconds)
    {
        lock (_segmentsLock)
        {
            TimeSpan cutoff = segmentEndTime - TimeSpan.FromSeconds(Math.Max(10, contextSeconds));
            var matching = _recentSegments.Where(s => s.End >= cutoff).ToList();
            if (matching.Count == 0)
            {
                matching = _recentSegments.TakeLast(5).ToList();
            }

            var sb = new System.Text.StringBuilder();
            foreach (var seg in matching)
            {
                sb.AppendLine($"[{seg.Start:hh\\:mm\\:ss}] {seg.Text}");
            }
            return sb.ToString();
        }
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

    [RelayCommand]
    private void ToggleMonitoring()
    {
        if (Status == AppStatus.Idle)
        {
            _sessionCoordinator.Start();
        }
        else
        {
            _sessionCoordinator.Stop();
        }
    }

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

    [RelayCommand]
    public void ToggleAi()
    {
        SetAiEnabled(!IsAiEnabled);
    }

    public void SetAiEnabled(bool enabled)
    {
        IsAiEnabled = enabled;
        _settings.TranscriptionEnabled = enabled;
        App.SaveSettings(_settings);

        _overlayViewModel.IsAiActive = enabled;

        if (Status == AppStatus.Recording)
        {
            if (enabled)
            {
                if (string.IsNullOrWhiteSpace(_settings.DashScopeApiKey))
                {
                    ExecuteOnUIThread(() =>
                    {
                        System.Windows.MessageBox.Show(
                            System.Windows.Application.Current?.MainWindow,
                            Resources.ApiKeyMissingPrompt,
                            Resources.Settings,
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    });
                    return;
                }

                _overlayViewModel.Clear();
                _transcriptionService.StartTranscription();

                if (_settings.ShowTranscriptionOverlay)
                {
                    ExecuteOnUIThread(() =>
                    {
                        if (_overlayWindow == null)
                        {
                            _overlayWindow = _serviceProvider.GetService(typeof(TranscriptionOverlayWindow)) as TranscriptionOverlayWindow;
                            if (_overlayWindow != null)
                            {
                                _overlayWindow.DataContext = _overlayViewModel;
                            }
                        }
                        _overlayViewModel.IsOverlayVisible = true;
                        _overlayWindow?.Show();
                    });
                }
            }
            else
            {
                _transcriptionService.StopTranscription();
                ExecuteOnUIThread(() =>
                {
                    _overlayViewModel.IsOverlayVisible = false;
                    _overlayWindow?.Hide();
                });
            }
        }
    }

    public void UpdateLanguage()
    {
        IsAiEnabled = _settings.TranscriptionEnabled;
        _overlayViewModel.IsAiActive = IsAiEnabled;
        OnPropertyChanged(nameof(AiFeatureLabel));
        OnPropertyChanged(nameof(AiToggleText));
        OnPropertyChanged(nameof(AiStatusText));
        OnPropertyChanged(nameof(SettingsButtonText));
        OnPropertyChanged(nameof(ExitButtonText));
        OnPropertyChanged(nameof(AppTitle));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(AppDescription));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(OutputFormatLabel));
        OnPropertyChanged(nameof(StartMonitoringText));
        OnPropertyChanged(nameof(StopMonitoringText));
        OnPropertyChanged(nameof(ToggleMonitoringText));
        OnPropertyChanged(nameof(StopRecordingText));
        OnPropertyChanged(nameof(OpenFolderText));
        OnPropertyChanged(nameof(ShowStatusWindowText));
        OnPropertyChanged(nameof(UploadToDriveText));
        UpdateStatusText();
    }

    private string? _currentAudioFilePath;

    private void OnRecordingRequested(object? sender, RecordingRequestedEventArgs e)
    {
        _currentAudioFilePath = e.AudioFilePath;
        _recorder.Start(e.AudioFilePath, _settings.OutputFormat);
        
        if (_settings.TranscriptionEnabled)
        {
            _overlayViewModel.Clear();
            _transcriptionService.StartTranscription();
            
            if (_settings.ShowTranscriptionOverlay)
            {
                ExecuteOnUIThread(() =>
                {
                    if (_overlayWindow == null)
                    {
                        _overlayWindow = _serviceProvider.GetService(typeof(TranscriptionOverlayWindow)) as TranscriptionOverlayWindow;
                        if (_overlayWindow != null)
                        {
                            _overlayWindow.DataContext = _overlayViewModel;
                        }
                    }
                    _overlayViewModel.IsOverlayVisible = true;
                    _overlayWindow?.Show();
                });
            }
        }
    }

    private void OnRecordingStopped(object? sender, EventArgs e)
    {
        _recorder.Stop();
        
        if (_transcriptionService.IsTranscribing || _settings.TranscriptionEnabled)
        {
            _transcriptionService.StopTranscription();
            ExecuteOnUIThread(() =>
            {
                _overlayViewModel.IsOverlayVisible = false;
                _overlayWindow?.Hide();
            });

            // Save transcript alongside audio recording
            if (_currentAudioFilePath != null)
            {
                var transcript = _transcriptionService.GetFullTranscript();
                if (transcript.Count > 0)
                {
                    string transcriptPath = Path.ChangeExtension(_currentAudioFilePath, ".txt");
                    try
                    {
                        using var writer = new StreamWriter(transcriptPath, false, Encoding.UTF8);
                        writer.WriteLine("================================================================================");
                        writer.WriteLine($"Meeting Transcript: {Path.GetFileName(_currentAudioFilePath)}");
                        writer.WriteLine($"Recorded At: {_dateTimeProvider.Now:yyyy-MM-dd HH:mm:ss}");
                        writer.WriteLine("================================================================================");
                        writer.WriteLine();
                        foreach (var segment in transcript)
                        {
                            writer.WriteLine($"[{segment.Start:hh\\:mm\\:ss} - {segment.End:hh\\:mm\\:ss}] {segment.Text}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to save transcript: {ex.Message}");
                    }
                }
            }
        }
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

    private void OnMicrophoneWarning(object? sender, string message)
    {
        ExecuteOnUIThread(() =>
        {
            _micWarningText = Resources.MicrophoneSilentWarning;
            UpdateStatusText();
        });
    }

    private void OnMicrophoneRestored(object? sender, EventArgs e)
    {
        ExecuteOnUIThread(() =>
        {
            _micWarningText = null;
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

        if (Status == AppStatus.Recording)
        {
            StatusText = !string.IsNullOrWhiteSpace(_micWarningText)
                ? $"{Resources.Recording} (⚠️ {_micWarningText})"
                : Resources.Recording;
            StatusCategory = StatusMessageCategory.Recording;
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

    private bool _hasCheckedUpdates;

    public void OnWindowLoaded()
    {
        if (!_hasCheckedUpdates)
        {
            _hasCheckedUpdates = true;
            if (_settings.AutoCheckUpdates)
            {
                _ = CheckForUpdatesOnStartupAsync();
            }
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var updateService = _serviceProvider.GetRequiredService<IUpdateService>();
            var updateInfo = await updateService.CheckForUpdatesAsync(CancellationToken.None);
            if (updateInfo != null)
            {
                if (updateInfo.Version != _settings.SkippedVersion)
                {
                    ExecuteOnUIThread(() =>
                    {
                        var updateWindow = _serviceProvider.GetRequiredService<UpdateWindow>();
                        updateWindow.Owner = System.Windows.Application.Current.MainWindow;
                        var vm = (UpdateViewModel)updateWindow.DataContext;
                        vm.Initialize(updateInfo);
                        updateWindow.ShowDialog();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check for updates on startup: {ex.Message}");
        }
    }

    private void OnOrganizeProgressChanged(object? sender, OrganizeProgressEventArgs e)
    {
        ExecuteOnUIThread(() =>
        {
            IsOrganizing = e.IsOrganizing;
            OrganizeStatusText = e.StatusText;
            OrganizeProgressValue = e.ProgressValue;
        });
    }

    private void OnPythonEnvSetupCompleted(object? sender, PythonEnvSetupCompletedEventArgs e)
    {
        ExecuteOnUIThread(() =>
        {
            bool isSettingsWindowOpen = false;
            try
            {
                isSettingsWindowOpen = System.Windows.Application.Current?.Windows.OfType<SettingsWindow>().Any(w => w.IsVisible) == true;
            }
            catch { }

            if (!isSettingsWindowOpen)
            {
                if (e.Success)
                {
                    App.ShowTrayNotification(
                        "Meeting Recorder",
                        "Python environment and notebooklm-py setup completed successfully.");
                }
                else
                {
                    App.ShowTrayNotification(
                        "Meeting Recorder",
                        $"Python environment setup failed: {e.ErrorMessage}");
                }
            }
        });
    }

    public void Dispose()
    {
        if (_pythonEnvSetupService != null)
        {
            _pythonEnvSetupService.SetupCompleted -= OnPythonEnvSetupCompleted;
        }

        _sessionCoordinator.RecordingRequested -= OnRecordingRequested;
        _sessionCoordinator.RecordingStopped -= OnRecordingStopped;
        _sessionCoordinator.StateChanged -= OnStateChanged;
        _cloudSyncService.UploadFailed -= OnUploadFailed;
        _cloudSyncService.UploadCompleted -= OnUploadCompleted;
        _cloudSyncService.OrganizeProgressChanged -= OnOrganizeProgressChanged;
        _recorder.AudioDataAvailable -= OnAudioDataAvailable;
        _recorder.MicrophoneWarning -= OnMicrophoneWarning;
        _recorder.MicrophoneRestored -= OnMicrophoneRestored;
        _transcriptionService.SegmentTranscribed -= OnSegmentTranscribed;
        _overlayViewModel.ToggleAiRequested -= ToggleAi;
        
        ExecuteOnUIThread(() =>
        {
            _overlayWindow?.Close();
        });
    }
}
