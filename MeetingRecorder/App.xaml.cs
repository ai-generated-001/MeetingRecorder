using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using H.NotifyIcon;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using MeetingRecorder.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;

namespace MeetingRecorder;

public partial class App : Application
{
    private TaskbarIcon? _notifyIcon;
    private ServiceProvider? _serviceProvider;

    internal static void ApplyUiLanguage(string languageCode)
    {
        var culture = string.IsNullOrWhiteSpace(languageCode)
            ? CultureInfo.InstalledUICulture
            : new CultureInfo(languageCode);

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        global::MeetingRecorder.Resources.Culture = culture;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigureServices();

        var settings = _serviceProvider!.GetRequiredService<AppSettings>();
        ApplyUiLanguage(settings.UiLanguage);

        base.OnStartup(e);

        _notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");
        _notifyIcon.DataContext = _serviceProvider!.GetRequiredService<MainViewModel>();

        using (var stream = new MemoryStream(AppResources.icon))
        {
            _notifyIcon.Icon = new Icon(stream);
        }

        _notifyIcon.ForceCreate();

        ShowMainWindow();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<AppSettings>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IFileIOService, FileIOService>();
        services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        services.AddSingleton<IGlobalHotkeyService>(_ => new GlobalHotkeyService(IntPtr.Zero));
        services.AddSingleton<IAudioSessionMonitor, AudioSessionDetector>();
        services.AddSingleton<IAudioRecorder, WasapiRecorder>();
        services.AddSingleton<INoteWriterService, NoteWriterService>();
        services.AddSingleton<SessionCoordinator>(sp =>
            new SessionCoordinator(
                sp.GetRequiredService<IAudioSessionMonitor>(),
                sp.GetRequiredService<IDateTimeProvider>(),
                TimeSpan.FromSeconds(sp.GetRequiredService<AppSettings>().DebounceSeconds)));

        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
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
            mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
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
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
