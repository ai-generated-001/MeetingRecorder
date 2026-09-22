using System.Collections.Specialized;
using System.Windows;
using MeetingRecorder.Services;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder;

public partial class LogWindow : Window
{
    private readonly AppLogger _appLogger;

    public LogWindow(LogViewModel viewModel, AppLogger appLogger)
    {
        _appLogger = appLogger;
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Subscribe for auto-scroll on new entries
        _appLogger.Entries.CollectionChanged += OnLogEntriesChanged;
        // Scroll to the end immediately to show the latest entries
        LogScrollViewer.ScrollToEnd();
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            LogScrollViewer.ScrollToEnd();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _appLogger.Entries.CollectionChanged -= OnLogEntriesChanged;
        base.OnClosed(e);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _appLogger.Entries.Clear();
    }
}
