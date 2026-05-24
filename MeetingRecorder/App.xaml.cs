using System;
using System.Drawing;
using System.IO;
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

        // Set the icon from AppResources
        using (var stream = new MemoryStream(AppResources.icon))
        {
            _notifyIcon.Icon = new Icon(stream);
        }

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
