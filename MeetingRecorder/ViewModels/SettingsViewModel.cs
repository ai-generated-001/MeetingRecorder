using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingRecorder.Models;
using MeetingRecorder.Services;

namespace MeetingRecorder.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ICloudSyncService _cloudSyncService;

    [ObservableProperty]
    private string _outputDirectory = "";

    [ObservableProperty]
    private string _uiLanguage = "";

    [ObservableProperty]
    private bool _googleDriveEnabled;

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
    private bool _isUiEnabled = true;

    public event EventHandler<bool>? RequestClose;

    public record LanguageItem(string DisplayName, string Code);

    public List<LanguageItem> SupportedLanguages { get; } =
    [
        new("English", ""),
        new("中文 (简体)", "zh-CN"),
    ];

    public SettingsViewModel(AppSettings settings, ICloudSyncService cloudSyncService)
    {
        _settings = settings;
        _cloudSyncService = cloudSyncService;

        // Initialize from settings
        OutputDirectory = string.IsNullOrWhiteSpace(_settings.OutputDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeetingRecordings")
            : _settings.OutputDirectory;
        UiLanguage = _settings.UiLanguage ?? "";
        GoogleDriveEnabled = _settings.GoogleDriveEnabled;
        GoogleClientId = _settings.GoogleClientId ?? "";
        GoogleClientSecret = _settings.GoogleClientSecret ?? "";
        GoogleDriveFolderPath = string.IsNullOrWhiteSpace(_settings.GoogleDriveFolderPath)
            ? "Meeting_Auto_Sync"
            : _settings.GoogleDriveFolderPath;

        // Asynchronously load the initial status
        _ = LoadStatusAsync();
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
    }

    [RelayCommand]
    private void ClearToken()
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

        // Clear the persisted folder ID since the user may sign in with
        // a different account whose Drive has different folder IDs.
        _settings.GoogleDriveFolderId = "";
        App.SaveSettings(_settings);

        // Also reset the in-memory DriveService so the next upload re-authenticates.
        (_cloudSyncService as GoogleDriveSyncService)?.ResetCredentials();

        GoogleDriveStatus = Resources.GoogleDriveNotSignedIn;
        GoogleDriveStatusForeground = System.Windows.Media.Brushes.Gray;

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
        _settings.GoogleDriveEnabled = GoogleDriveEnabled;
        _settings.GoogleClientId = GoogleClientId;
        _settings.GoogleClientSecret = clientSecret;
        _settings.GoogleDriveFolderPath = string.IsNullOrWhiteSpace(GoogleDriveFolderPath)
            ? "Meeting_Auto_Sync"
            : GoogleDriveFolderPath.Trim();

        if (credentialsChanged)
        {
            _settings.GoogleDriveFolderId = "";
            (_cloudSyncService as GoogleDriveSyncService)?.ResetCredentials();
        }

        App.ApplyUiLanguage(_settings.UiLanguage);
        App.SaveSettings(_settings);

        RequestClose?.Invoke(this, true);
    }
}
