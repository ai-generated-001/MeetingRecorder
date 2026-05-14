using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MeetingRecorder;

public partial class SettingsWindow : Window
{
    public string OutputDirectory { get; private set; }

    public SettingsWindow(string currentOutputDirectory)
    {
        InitializeComponent();
        OutputDirectory = string.IsNullOrWhiteSpace(currentOutputDirectory)
            ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "MeetingRecordings")
            : currentOutputDirectory;
        OutputDirectoryTextBox.Text = OutputDirectory;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select where recordings will be saved.",
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
            MessageBox.Show(this, "Please choose a valid folder.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OutputDirectory = selected;
        DialogResult = true;
        Close();
    }
}
