using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;
using MeetingRecorder.Services;

namespace MeetingRecorder;

public partial class SettingsWindow : Window
{
    private record LanguageItem(string DisplayName, string Code);

    private static readonly List<LanguageItem> SupportedLanguages =
    [
        new("English", ""),
        new("中文 (简体)", "zh-CN"),
    ];

    /// <summary>Action invoked when the user clicks "Clear saved token".</summary>
    private readonly Action? _clearTokenAction;

    public string OutputDirectory { get; private set; }
    public string UiLanguage { get; private set; }
    public bool GoogleDriveEnabled { get; private set; }
    public string GoogleClientId { get; private set; }
    public string GoogleClientSecret { get; private set; }
    public string GoogleDriveFolderPath { get; private set; }

    public SettingsWindow(
        string currentOutputDirectory,
        string currentUiLanguage,
        bool currentGoogleDriveEnabled = false,
        string currentGoogleClientId = "",
        string currentGoogleClientSecret = "",
        string currentGoogleDriveFolderPath = "Meeting_Auto_Sync",
        Action? clearTokenAction = null,
        Func<CancellationToken, Task<string>>? getLoginStatusFunc = null)
    {
        InitializeComponent();

        _clearTokenAction = clearTokenAction;

        // Fetch login status asynchronously
        if (getLoginStatusFunc != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var status = await getLoginStatusFunc(CancellationToken.None);
                    Dispatcher.Invoke(() =>
                    {
                        StatusValueTextBlock.Text = status;
                        if (status == global::MeetingRecorder.Resources.GoogleDriveNotSignedIn)
                        {
                            StatusValueTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
                        }
                        else
                        {
                            StatusValueTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error fetching login status: {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        StatusValueTextBlock.Text = global::MeetingRecorder.Resources.GoogleDriveNotSignedIn;
                        StatusValueTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
                    });
                }
            });
        }
        else
        {
            StatusValueTextBlock.Text = global::MeetingRecorder.Resources.GoogleDriveNotSignedIn;
            StatusValueTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
        }

        OutputDirectory = string.IsNullOrWhiteSpace(currentOutputDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeetingRecordings")
            : currentOutputDirectory;
        UiLanguage = currentUiLanguage ?? "";
        GoogleDriveEnabled = currentGoogleDriveEnabled;
        GoogleClientId = currentGoogleClientId ?? "";
        GoogleClientSecret = currentGoogleClientSecret ?? "";
        GoogleDriveFolderPath = string.IsNullOrWhiteSpace(currentGoogleDriveFolderPath)
            ? "Meeting_Auto_Sync"
            : currentGoogleDriveFolderPath;

        OutputDirectoryTextBox.Text = OutputDirectory;

        LanguageComboBox.ItemsSource = SupportedLanguages;
        var selected = SupportedLanguages.Find(l => l.Code == UiLanguage) ?? SupportedLanguages[0];
        LanguageComboBox.SelectedItem = selected;

        GoogleDriveEnabledCheckBox.IsChecked = GoogleDriveEnabled;
        ClientIdTextBox.Text = GoogleClientId;
        // PasswordBox doesn't support two-way binding, set programmatically
        ClientSecretPasswordBox.Password = GoogleClientSecret;
        DriveFolderPathTextBox.Text = GoogleDriveFolderPath;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = global::MeetingRecorder.Resources.SelectFolderDescription,
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(OutputDirectoryTextBox.Text)
                ? OutputDirectoryTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OutputDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        _clearTokenAction?.Invoke();
        StatusValueTextBlock.Text = global::MeetingRecorder.Resources.GoogleDriveNotSignedIn;
        StatusValueTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
        System.Windows.MessageBox.Show(
            this,
            global::MeetingRecorder.Resources.TokenClearedMessage,
            global::MeetingRecorder.Resources.Settings,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selected = OutputDirectoryTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(selected))
        {
            System.Windows.MessageBox.Show(
                this,
                global::MeetingRecorder.Resources.SelectValidFolder,
                global::MeetingRecorder.Resources.Settings,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        OutputDirectory = selected;
        UiLanguage = (LanguageComboBox.SelectedItem as LanguageItem)?.Code ?? "";
        GoogleDriveEnabled = GoogleDriveEnabledCheckBox.IsChecked == true;
        GoogleClientId = ClientIdTextBox.Text.Trim();
        GoogleClientSecret = ClientSecretPasswordBox.Password;
        GoogleDriveFolderPath = string.IsNullOrWhiteSpace(DriveFolderPathTextBox.Text)
            ? "Meeting_Auto_Sync"
            : DriveFolderPathTextBox.Text.Trim();

        DialogResult = true;
        Close();
    }
}
