using System;
using System.ComponentModel;
using System.Windows;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder;

public partial class UpdateWindow : Window
{
    private readonly UpdateViewModel _viewModel;

    public UpdateWindow(UpdateViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.RequestClose += (sender, e) =>
        {
            try
            {
                this.DialogResult = true;
            }
            catch (InvalidOperationException)
            {
                // DialogResult can only be set if the window was shown as a dialog.
                // Fallback for normal Show() call.
            }
            this.Close();
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsDownloading)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "An update is currently downloading. Are you sure you want to cancel and exit?",
                "Cancel Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.CancelDownload();
            }
            else
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnClosing(e);
    }
}
