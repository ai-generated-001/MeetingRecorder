using System.ComponentModel;
using System.Windows;

namespace MeetingRecorder;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Instead of closing, just hide the window
        e.Cancel = true;
        this.Hide();
        base.OnClosing(e);
    }
}
