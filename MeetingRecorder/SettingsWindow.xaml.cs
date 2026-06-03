using System;
using System.Collections.Generic;
using System.IO;
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
    public string GoogleClientId { get; private set; }
    public string GoogleClientSecret { get; private set; }

    public SettingsWindow(
        string currentOutputDirectory,
        string currentUiLanguage,
        string currentGoogleClientId = "",
        string currentGoogleClientSecret = "",
        Action? clearTokenAction = null)
    {
        InitializeComponent();

        _clearTokenAction = clearTokenAction;

        OutputDirectory = string.IsNullOrWhiteSpace(currentOutputDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeetingRecordings")
            : currentOutputDirectory;
        UiLanguage = currentUiLanguage ?? "";
        GoogleClientId = currentGoogleClientId ?? "";
        GoogleClientSecret = currentGoogleClientSecret ?? "";

        OutputDirectoryTextBox.Text = OutputDirectory;

        LanguageComboBox.ItemsSource = SupportedLanguages;
        var selected = SupportedLanguages.Find(l => l.Code == UiLanguage) ?? SupportedLanguages[0];
        LanguageComboBox.SelectedItem = selected;

        ClientIdTextBox.Text = GoogleClientId;
        // PasswordBox doesn't support two-way binding, set programmatically
        ClientSecretPasswordBox.Password = GoogleClientSecret;
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
        GoogleClientId = ClientIdTextBox.Text.Trim();
        GoogleClientSecret = ClientSecretPasswordBox.Password;

        DialogResult = true;
        Close();
    }
}
