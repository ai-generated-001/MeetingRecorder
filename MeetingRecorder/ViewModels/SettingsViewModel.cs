using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using MeetingRecorder.Models;
using MeetingRecorder.Services;

namespace MeetingRecorder.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ICloudSyncService _cloudSyncService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUpdateService _updateService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IPythonEnvSetupService _pythonEnvSetupService;

    [ObservableProperty]
    private bool _autoCheckUpdates;

    [ObservableProperty]
    private string _updateStatusText = "";


    [ObservableProperty]
    private string _outputDirectory = "";

    [ObservableProperty]
    private double _minFileSizeMb;

    [ObservableProperty]
    private string _selectedMicrophoneDeviceId = "";

    [ObservableProperty]
    private string _uiLanguage = "";

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private bool _googleDriveEnabled;

    [ObservableProperty]
    private bool _startWithWindows;

    // Transcription & AI Insights properties
    [ObservableProperty]
    private bool _transcriptionEnabled;

    [ObservableProperty]
    private string _dashScopeApiKey = "";

    [ObservableProperty]
    private string _dashScopeBaseUrl = "https://dashscope.aliyuncs.com";

    [ObservableProperty]
    private string _transcriptionLanguage = "auto";

    [ObservableProperty]
    private bool _showTranscriptionOverlay;

    [ObservableProperty]
    private bool _insightsEnabled = true;

    [ObservableProperty]
    private string _mentionNamesText = "";

    [ObservableProperty]
    private int _insightContextSeconds = 30;

    [ObservableProperty]
    private string _qwenModel = "qwen-turbo";

    [ObservableProperty]
    private string _vocabularyId = "";

    [ObservableProperty]
    private string _hotwordsText = "";

    [ObservableProperty]
    private string _compileHotwordsStatus = "";

    [ObservableProperty]
    private System.Windows.Media.Brush _compileHotwordsStatusForeground = System.Windows.Media.Brushes.Gray;

    [ObservableProperty]
    private string _testConnectionStatus = "";

    [ObservableProperty]
    private System.Windows.Media.Brush _testConnectionStatusForeground = System.Windows.Media.Brushes.Gray;

    [ObservableProperty]
    private string _googleClientId = "";

    [ObservableProperty]
    private string _googleClientSecret = "";

    [ObservableProperty]
    private string _googleDriveFolderPath = "";

    [ObservableProperty]
    private string _googleDriveStatus = "";

    [ObservableProperty]
    private System.Windows.Media.Brush _googleDriveStatusForeground = System.Windows.Media.Brushes.Gray;

    [ObservableProperty]
    private bool _notebookLmEnabled;

    [ObservableProperty]
    private string _notebookLmNotebookPattern = "Meetings {Year}-{Month}";

    [ObservableProperty]
    private string _notebookLmCliPath = "";

    [ObservableProperty]
    private bool _canEnableNotebookLm;

    [ObservableProperty]
    private string _notebookLmCliStatus = "";

    [ObservableProperty]
    private System.Windows.Media.Brush _notebookLmCliStatusForeground = System.Windows.Media.Brushes.Gray;

    [ObservableProperty]
    private bool _isPythonEnvReady;

    [ObservableProperty]
    private bool _isSettingUpPythonEnv;

    [ObservableProperty]
    private string _pythonEnvProgressText = "";

    [ObservableProperty]
    private bool _hasHostPython;

    [ObservableProperty]
    private bool _isUiEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOrganizeProgress))]
    private bool _isOrganizing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOrganizeProgress))]
    private string _organizeStatusText = "";

    [ObservableProperty]
    private int _organizeProgressValue;

    public bool ShowOrganizeProgress => IsOrganizing || !string.IsNullOrWhiteSpace(OrganizeStatusText);

    public event EventHandler<bool>? RequestClose;

    public record LanguageItem(string DisplayName, string Code);
    public record ThemeItem(string DisplayName, string Code);
    public record AudioDeviceItem(string DisplayName, string Id);

    public ObservableCollection<AudioDeviceItem> AvailableMicrophones { get; } = new();

    public List<LanguageItem> SupportedLanguages { get; } =
    [
        new("English", ""),
        new("中文 (简体)", "zh-CN"),
    ];

    public string ThemeLabel => Resources.ThemeLabel;

    public List<ThemeItem> SupportedThemes =>
    [
        new(Resources.ThemeSystem, "System"),
        new(Resources.ThemeLight, "Light"),
        new(Resources.ThemeDark, "Dark")
    ];

    public List<string> SupportedQwenModels { get; } = ["qwen-turbo", "qwen-plus", "qwen-max"];

    public List<LanguageItem> SupportedTranscriptionLanguages { get; } =
    [
        new("Auto Detect", "auto"),
        new("Chinese (中文)", "zh"),
        new("English", "en"),
        new("Japanese (日本語)", "ja"),
        new("Korean (한국어)", "ko"),
        new("French (Français)", "fr"),
        new("German (Deutsch)", "de"),
        new("Spanish (Español)", "es")
    ];

    private readonly IInsightService _insightService;
    private readonly IDashScopePhraseService _phraseService;

    public SettingsViewModel(
        AppSettings settings,
        ICloudSyncService cloudSyncService,
        IServiceProvider serviceProvider,
        IUpdateService updateService,
        ITranscriptionService transcriptionService,
        IInsightService insightService,
        IDashScopePhraseService phraseService,
        IPythonEnvSetupService pythonEnvSetupService)
    {
        _settings = settings;
        _cloudSyncService = cloudSyncService;
        _serviceProvider = serviceProvider;
        _updateService = updateService;
        _transcriptionService = transcriptionService;
        _insightService = insightService;
        _phraseService = phraseService;
        _pythonEnvSetupService = pythonEnvSetupService;

        // Initialize from settings
        OutputDirectory = string.IsNullOrWhiteSpace(_settings.OutputDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeetingRecordings")
            : _settings.OutputDirectory;
        UiLanguage = _settings.UiLanguage ?? "";
        SelectedTheme = _settings.Theme ?? "System";
        GoogleDriveEnabled = _settings.GoogleDriveEnabled;
        GoogleClientId = _settings.GoogleClientId ?? "";
        GoogleClientSecret = _settings.GoogleClientSecret ?? "";
        GoogleDriveFolderPath = string.IsNullOrWhiteSpace(_settings.GoogleDriveFolderPath)
            ? "Meeting_Auto_Sync"
            : _settings.GoogleDriveFolderPath;
        StartWithWindows = _settings.StartWithWindows;
        AutoCheckUpdates = _settings.AutoCheckUpdates;
        MinFileSizeMb = _settings.MinFileSizeMb;

        TranscriptionEnabled = _settings.TranscriptionEnabled;
        DashScopeApiKey = _settings.DashScopeApiKey ?? "";
        DashScopeBaseUrl = string.IsNullOrWhiteSpace(_settings.DashScopeBaseUrl) ? "https://dashscope.aliyuncs.com" : _settings.DashScopeBaseUrl;
        TranscriptionLanguage = _settings.TranscriptionLanguage ?? "auto";
        ShowTranscriptionOverlay = _settings.ShowTranscriptionOverlay;
        VocabularyId = _settings.VocabularyId ?? "";
        HotwordsText = _settings.Hotwords ?? "";

        NotebookLmEnabled = _settings.NotebookLmEnabled;
        NotebookLmNotebookPattern = string.IsNullOrWhiteSpace(_settings.NotebookLmNotebookPattern)
            ? "Meetings {Year}-{Month}"
            : _settings.NotebookLmNotebookPattern;
        NotebookLmCliPath = _settings.NotebookLmCliPath ?? "";

        InsightsEnabled = _settings.InsightsEnabled;
        MentionNamesText = string.Join(", ", _settings.MentionNames ?? new List<string>());
        InsightContextSeconds = _settings.InsightContextSeconds <= 0 ? 30 : _settings.InsightContextSeconds;
        QwenModel = string.IsNullOrWhiteSpace(_settings.QwenModel) ? "qwen-turbo" : _settings.QwenModel;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        UpdateStatusText = string.Format("Version: {0}", version?.ToString() ?? "1.0.0.0");

        LoadMicrophones();

        // Asynchronously load the initial status
        _ = LoadStatusAsync();

        IsOrganizing = _cloudSyncService.IsOrganizing;
        OrganizeStatusText = _cloudSyncService.OrganizeStatusText;
        OrganizeProgressValue = _cloudSyncService.OrganizeProgressValue;
        _cloudSyncService.OrganizeProgressChanged += OnOrganizeProgressChanged;
    }

    public void LoadMicrophones()
    {
        AvailableMicrophones.Clear();
        AvailableMicrophones.Add(new AudioDeviceItem(Resources.MicrophoneDefault, ""));

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in endpoints)
            {
                AvailableMicrophones.Add(new AudioDeviceItem(device.FriendlyName, device.ID));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsViewModel] Failed to enumerate capture devices: {ex.Message}");
        }

        SelectedMicrophoneDeviceId = AvailableMicrophones.Any(m => m.Id == _settings.MicrophoneDeviceId)
            ? _settings.MicrophoneDeviceId
            : "";
    }

    [RelayCommand]
    private async Task CompileHotwordsAsync(object? parameter)
    {
        var passwordBox = parameter as System.Windows.Controls.PasswordBox;
        string key = passwordBox?.Password?.Trim() ?? DashScopeApiKey.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            CompileHotwordsStatus = "API Key is required to compile hotwords.";
            CompileHotwordsStatusForeground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        // Parse hotwords from both HotwordsText and MentionNamesText
        var mentionList = (MentionNamesText ?? "")
            .Split(new[] { ',', ';', '\n', '\r', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s));

        var phrases = DashScopePhraseService.ParseHotwords(HotwordsText, mentionList);
        if (phrases.Count == 0)
        {
            CompileHotwordsStatus = "Please enter at least one hotword or mention name.";
            CompileHotwordsStatusForeground = System.Windows.Media.Brushes.Orange;
            return;
        }

        IsUiEnabled = false;
        CompileHotwordsStatus = $"Compiling {phrases.Count} hotwords on DashScope...";
        CompileHotwordsStatusForeground = System.Windows.Media.Brushes.Orange;

        try
        {
            string? resultId = await _phraseService.CreatePhrasesAsync(key, DashScopeBaseUrl, "paraformer-realtime-v1", phrases, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(resultId))
            {
                VocabularyId = resultId;
                _settings.VocabularyId = resultId;
                CompileHotwordsStatus = $"Hotwords compiled! ID: {resultId} ✅";
                CompileHotwordsStatusForeground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                CompileHotwordsStatus = "Compilation returned no ID. ❌";
                CompileHotwordsStatusForeground = System.Windows.Media.Brushes.Red;
            }
        }
        catch (Exception ex)
        {
            CompileHotwordsStatus = $"Compilation failed: {ex.Message}";
            CompileHotwordsStatusForeground = System.Windows.Media.Brushes.Red;
        }
        finally
        {
            IsUiEnabled = true;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync(object? parameter)
    {
        var passwordBox = parameter as System.Windows.Controls.PasswordBox;
        string key = passwordBox?.Password?.Trim() ?? DashScopeApiKey.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            TestConnectionStatus = "API Key is empty.";
            TestConnectionStatusForeground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        IsUiEnabled = false;
        TestConnectionStatus = "Testing connection...";
        TestConnectionStatusForeground = System.Windows.Media.Brushes.Orange;

        try
        {
            if (_insightService is QwenInsightService qwenService)
            {
                bool ok = await qwenService.TestConnectionAsync(key, DashScopeBaseUrl, QwenModel, CancellationToken.None);
                if (ok)
                {
                    TestConnectionStatus = "Connection successful! ✅";
                    TestConnectionStatusForeground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    TestConnectionStatus = "Connection failed. Please check key & base URL. ❌";
                    TestConnectionStatusForeground = System.Windows.Media.Brushes.Red;
                }
            }
            else
            {
                TestConnectionStatus = "Ready";
                TestConnectionStatusForeground = System.Windows.Media.Brushes.Green;
            }
        }
        catch (Exception ex)
        {
            TestConnectionStatus = $"Connection error: {ex.Message}";
            TestConnectionStatusForeground = System.Windows.Media.Brushes.Red;
        }
        finally
        {
            IsUiEnabled = true;
        }
    }

    [RelayCommand]
    private void Browse()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = Resources.SelectFolderDescription,
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(OutputDirectory)
                ? OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OutputDirectory = dialog.SelectedPath;
        }
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            if (_cloudSyncService is GoogleDriveSyncService syncService)
            {
                var status = await syncService.GetAccountStatusStringAsync(CancellationToken.None);
                GoogleDriveStatus = status;
                GoogleDriveStatusForeground = status == Resources.GoogleDriveNotSignedIn
                    ? System.Windows.Media.Brushes.Gray
                    : System.Windows.Media.Brushes.Green;
            }
            else
            {
                GoogleDriveStatus = Resources.GoogleDriveNotSignedIn;
                GoogleDriveStatusForeground = System.Windows.Media.Brushes.Gray;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching login status: {ex.Message}");
            GoogleDriveStatus = Resources.GoogleDriveNotSignedIn;
            GoogleDriveStatusForeground = System.Windows.Media.Brushes.Gray;
        }

        UpdateNotebookLmState();
    }

    private void UpdateNotebookLmState()
    {
        // Must have credentials and be signed in (dpapi_user.dat exists)
        bool hasCreds = NotebookLmSyncService.HasGoogleOAuthCredentials(_settings);
        bool isAuthenticated = NotebookLmSyncService.IsGoogleUserAuthenticated();
        CanEnableNotebookLm = hasCreds && isAuthenticated;

        if (!CanEnableNotebookLm && NotebookLmEnabled)
        {
            NotebookLmEnabled = false;
        }

        IsPythonEnvReady = _pythonEnvSetupService.IsVenvReady();

        var cliPath = NotebookLmSyncService.FindNotebookLmExecutable(NotebookLmCliPath);
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            NotebookLmCliStatus = "CLI not found";
            NotebookLmCliStatusForeground = System.Windows.Media.Brushes.Orange;
        }
        else
        {
            bool isVenv = !string.IsNullOrWhiteSpace(App.PythonEnvFolderPath) &&
                          cliPath.StartsWith(App.PythonEnvFolderPath, StringComparison.OrdinalIgnoreCase);
            NotebookLmCliStatus = isVenv
                ? "Ready (managed venv)"
                : $"CLI detected: {Path.GetFileName(cliPath)}";
            NotebookLmCliStatusForeground = System.Windows.Media.Brushes.Green;
        }

        // Asynchronously check for host python
        _ = CheckHostPythonAsync();
    }

    private async Task CheckHostPythonAsync()
    {
        var host = await _pythonEnvSetupService.FindHostPythonExecutableAsync();
        HasHostPython = !string.IsNullOrWhiteSpace(host);
    }

    [RelayCommand]
    private async Task SetupPythonEnvAsync()
    {
        if (IsSettingUpPythonEnv) return;

        IsSettingUpPythonEnv = true;
        PythonEnvProgressText = "Starting setup...";

        var progress = new Progress<string>(msg =>
        {
            PythonEnvProgressText = msg;
        });

        try
        {
            await _pythonEnvSetupService.SetupEnvironmentAsync(progress, CancellationToken.None);
            UpdateNotebookLmState();

            System.Windows.MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                "Python environment and notebooklm-py setup successfully!",
                "NotebookLM Environment Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsViewModel] Python environment setup failed: {ex.Message}");
            PythonEnvProgressText = $"Error: {ex.Message}";

            System.Windows.MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                $"Failed to set up Python environment:\n{ex.Message}",
                "Setup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsSettingUpPythonEnv = false;
            UpdateNotebookLmState();
        }
    }

    [RelayCommand]
    private void OpenPythonDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.python.org/downloads/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open python website: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearToken()
    {
        // Wipe every encrypted token file in the token.json folder.
        var tokenDir = App.TokenFolderPath;
        if (Directory.Exists(tokenDir))
        {
            foreach (var file in Directory.GetFiles(tokenDir, "dpapi_*.dat"))
            {
                try { File.Delete(file); }
                catch { /* best-effort */ }
            }
        }

        // Clear the persisted folder ID since the user may sign in with
        // a different account whose Drive has different folder IDs.
        _settings.GoogleDriveFolderId = "";
        _settings.NotebookLmEnabled = false;
        NotebookLmEnabled = false;
        App.SaveSettings(_settings);

        // Also reset the in-memory DriveService so the next upload re-authenticates.
        (_cloudSyncService as GoogleDriveSyncService)?.ResetCredentials();

        GoogleDriveStatus = Resources.GoogleDriveNotSignedIn;
        GoogleDriveStatusForeground = System.Windows.Media.Brushes.Gray;
        UpdateNotebookLmState();

        System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            Resources.TokenClearedMessage,
            Resources.Settings,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task LoginAsync(object? parameter)
    {
        var passwordBox = parameter as System.Windows.Controls.PasswordBox;
        string clientId = GoogleClientId.Trim();
        string clientSecret = passwordBox?.Password ?? "";

        IsUiEnabled = false;
        GoogleDriveStatus = Resources.GoogleDriveSigningIn;
        GoogleDriveStatusForeground = System.Windows.Media.Brushes.Orange;

        try
        {
            if (_cloudSyncService is GoogleDriveSyncService syncService)
            {
                var status = await syncService.LoginAsync(clientId, clientSecret, CancellationToken.None);
                GoogleDriveStatus = status;
                GoogleDriveStatusForeground = System.Windows.Media.Brushes.Green;

                System.Windows.MessageBox.Show(
                    System.Windows.Application.Current.MainWindow,
                    Resources.GoogleDriveLoginSuccess,
                    Resources.Settings,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            await RefreshLoginStatusAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during Google Drive login: {ex.Message}");
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                string.Format(Resources.GoogleDriveLoginFailed, ex.Message),
                Resources.Settings,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            await RefreshLoginStatusAsync();
        }
        finally
        {
            IsUiEnabled = true;
        }
    }

    private async Task RefreshLoginStatusAsync()
    {
        try
        {
            if (_cloudSyncService is GoogleDriveSyncService syncService)
            {
                var status = await syncService.GetAccountStatusStringAsync(CancellationToken.None);
                GoogleDriveStatus = status;
                GoogleDriveStatusForeground = status == Resources.GoogleDriveNotSignedIn
                    ? System.Windows.Media.Brushes.Gray
                    : System.Windows.Media.Brushes.Green;
            }
            else
            {
                GoogleDriveStatus = Resources.GoogleDriveNotSignedIn;
                GoogleDriveStatusForeground = System.Windows.Media.Brushes.Gray;
            }
        }
        catch
        {
            GoogleDriveStatus = Resources.GoogleDriveNotSignedIn;
            GoogleDriveStatusForeground = System.Windows.Media.Brushes.Gray;
        }

        UpdateNotebookLmState();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatusText = Resources.CheckingForUpdates;
        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync(CancellationToken.None);
            if (updateInfo != null)
            {
                UpdateStatusText = string.Format("New version available: v{0}", updateInfo.Version);

                // Find active window for ownership
                Window? activeWindow = null;
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window.IsActive)
                    {
                        activeWindow = window;
                        break;
                    }
                }

                var updateWindow = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<UpdateWindow>(_serviceProvider);
                updateWindow.Owner = activeWindow ?? System.Windows.Application.Current.MainWindow;
                var vm = (UpdateViewModel)updateWindow.DataContext;
                vm.Initialize(updateInfo);
                updateWindow.ShowDialog();
            }
            else
            {
                UpdateStatusText = Resources.UpToDate;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = string.Format(Resources.UpdateFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void Save(object? parameter)
    {
        var passwordBox = parameter as System.Windows.Controls.PasswordBox;
        var clientSecret = passwordBox?.Password ?? GoogleClientSecret;

        var selected = OutputDirectory?.Trim();
        if (string.IsNullOrWhiteSpace(selected))
        {
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                Resources.SelectValidFolder,
                Resources.Settings,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        bool credentialsChanged =
            _settings.GoogleDriveEnabled != GoogleDriveEnabled ||
            _settings.GoogleClientId != GoogleClientId ||
            _settings.GoogleClientSecret != clientSecret ||
            _settings.GoogleDriveFolderPath != GoogleDriveFolderPath;

        _settings.OutputDirectory = selected;
        _settings.UiLanguage = UiLanguage;
        _settings.Theme = SelectedTheme;
        _settings.GoogleDriveEnabled = GoogleDriveEnabled;
        _settings.GoogleClientId = GoogleClientId;
        _settings.GoogleClientSecret = clientSecret;
        _settings.GoogleDriveFolderPath = string.IsNullOrWhiteSpace(GoogleDriveFolderPath)
            ? "Meeting_Auto_Sync"
            : GoogleDriveFolderPath.Trim();

        _settings.StartWithWindows = StartWithWindows;
        _settings.AutoCheckUpdates = AutoCheckUpdates;
        _settings.MinFileSizeMb = MinFileSizeMb;
        _settings.MicrophoneDeviceId = SelectedMicrophoneDeviceId ?? "";

        _settings.TranscriptionEnabled = TranscriptionEnabled;
        _settings.DashScopeApiKey = DashScopeApiKey;
        _settings.DashScopeBaseUrl = string.IsNullOrWhiteSpace(DashScopeBaseUrl) ? "https://dashscope.aliyuncs.com" : DashScopeBaseUrl.Trim();
        _settings.TranscriptionLanguage = TranscriptionLanguage;
        _settings.ShowTranscriptionOverlay = ShowTranscriptionOverlay;
        _settings.VocabularyId = VocabularyId?.Trim() ?? "";
        _settings.Hotwords = HotwordsText?.Trim() ?? "";

        _settings.InsightsEnabled = InsightsEnabled;
        _settings.MentionNames = (MentionNamesText ?? "")
            .Split(new[] { ',', ';', '\n', '\r', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
        _settings.InsightContextSeconds = Math.Max(5, Math.Min(300, InsightContextSeconds));
        _settings.QwenModel = string.IsNullOrWhiteSpace(QwenModel) ? "qwen-turbo" : QwenModel.Trim();

        _settings.NotebookLmEnabled = CanEnableNotebookLm && NotebookLmEnabled;
        _settings.NotebookLmNotebookPattern = string.IsNullOrWhiteSpace(NotebookLmNotebookPattern)
            ? "Meetings {Year}-{Month}"
            : NotebookLmNotebookPattern.Trim();
        _settings.NotebookLmCliPath = NotebookLmCliPath?.Trim() ?? "";

        if (credentialsChanged)
        {
            _settings.GoogleDriveFolderId = "";
            (_cloudSyncService as GoogleDriveSyncService)?.ResetCredentials();
        }

        App.ApplyUiLanguage(_settings.UiLanguage);
        App.ApplyTheme(_settings.Theme);
        App.ApplyStartupSetting(_settings.StartWithWindows);
        App.SaveSettings(_settings);

        RequestClose?.Invoke(this, true);
    }

    [RelayCommand(CanExecute = nameof(CanOrganizeFiles))]
    private void OrganizeFiles()
    {
        _cloudSyncService.StartOrganizeExistingFiles();
    }

    private bool CanOrganizeFiles() => !IsOrganizing;

    private void OnOrganizeProgressChanged(object? sender, OrganizeProgressEventArgs e)
    {
        ExecuteOnUIThread(() =>
        {
            IsOrganizing = e.IsOrganizing;
            OrganizeStatusText = e.StatusText;
            OrganizeProgressValue = e.ProgressValue;
            OrganizeFilesCommand.NotifyCanExecuteChanged();
        });
    }

    private void ExecuteOnUIThread(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
        {
            action();
        }
        else
        {
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(action);
        }
    }

    public void Cleanup()
    {
        _cloudSyncService.OrganizeProgressChanged -= OnOrganizeProgressChanged;
    }
}
