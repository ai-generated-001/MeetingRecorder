using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
    }

    protected override void OnContentRendered(System.EventArgs e)
    {
        base.OnContentRendered(e);
        _viewModel.OnWindowLoaded();

        // Subscribe to log entry additions so the ScrollViewer stays at the bottom.
        _viewModel.LogEntries.CollectionChanged += OnLogEntriesChanged;
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            LogScrollViewer.ScrollToEnd();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Instead of closing, just hide the window
        e.Cancel = true;
        this.Hide();
        base.OnClosing(e);
    }

    private void MicrophoneComboBox_DropDownOpened(object sender, System.EventArgs e)
    {
        _viewModel.RefreshMicrophones();
    }
}
