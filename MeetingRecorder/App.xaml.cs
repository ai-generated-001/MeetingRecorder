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
    }

    private void Show_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = MainWindow as MainWindow;
        if (mainWindow == null)
        {
            mainWindow = new MainWindow();
        }
        mainWindow.Show();
        mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}
