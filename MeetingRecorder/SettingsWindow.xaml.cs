using System.Windows;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // PasswordBox doesn't support two-way data binding for security reasons,
        // so we set the initial value programmatically from the ViewModel.
        ClientSecretPasswordBox.Password = viewModel.GoogleClientSecret;

        // Listen for the ViewModel's request to close the dialog
        viewModel.RequestClose += (sender, dialogResult) =>
        {
            this.DialogResult = dialogResult;
            this.Close();
        };
    }
}
