using System;
using System.Drawing;
using System.Windows.Interop;
using System.Windows;
using H.NotifyIcon;

namespace MeetingRecorder;

public partial class App : Application
{
    private TaskbarIcon? _notifyIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Find the icon in the resources
        _notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");
        _notifyIcon.Icon = SystemIcons.Application;
        _notifyIcon.ForceCreate();

        ShowMainWindow();
    }

    private void Show_Click(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        var mainWindow = MainWindow as MainWindow;
        if (mainWindow == null)
        {
            mainWindow = new MainWindow();
            MainWindow = mainWindow;
        }

        const double margin = 16;
        var workArea = SystemParameters.WorkArea;
        mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        mainWindow.Left = workArea.Right - mainWindow.Width - margin;
        mainWindow.Top = workArea.Bottom - mainWindow.Height - margin;

        mainWindow.Show();
        mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}
