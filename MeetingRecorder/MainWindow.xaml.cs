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
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Instead of closing, just hide the window
        e.Cancel = true;
        this.Hide();
        base.OnClosing(e);
    }
}
