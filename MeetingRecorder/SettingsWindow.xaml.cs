using System.Collections.Generic;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MeetingRecorder;

public partial class SettingsWindow : Window
{
    private record LanguageItem(string DisplayName, string Code);

    private static readonly List<LanguageItem> SupportedLanguages =
    [
        new("English", ""),
        new("中文 (简体)", "zh-CN"),
    ];

    public string OutputDirectory { get; private set; }
    public string UiLanguage { get; private set; }

    public SettingsWindow(string currentOutputDirectory, string currentUiLanguage)
    {
        InitializeComponent();
        OutputDirectory = string.IsNullOrWhiteSpace(currentOutputDirectory)
            ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "MeetingRecordings")
            : currentOutputDirectory;
        UiLanguage = currentUiLanguage ?? "";

        OutputDirectoryTextBox.Text = OutputDirectory;

        LanguageComboBox.ItemsSource = SupportedLanguages;
        var selected = SupportedLanguages.Find(l => l.Code == UiLanguage) ?? SupportedLanguages[0];
        LanguageComboBox.SelectedItem = selected;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = global::MeetingRecorder.Resources.SelectFolderDescription,
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(OutputDirectoryTextBox.Text)
                ? OutputDirectoryTextBox.Text
                : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OutputDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selected = OutputDirectoryTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(selected))
        {
            System.Windows.MessageBox.Show(this, global::MeetingRecorder.Resources.SelectValidFolder, global::MeetingRecorder.Resources.Settings, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OutputDirectory = selected;
        UiLanguage = (LanguageComboBox.SelectedItem as LanguageItem)?.Code ?? "";
        DialogResult = true;
        Close();
    }
}
