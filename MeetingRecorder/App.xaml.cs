using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using H.NotifyIcon;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using MeetingRecorder.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Application = System.Windows.Application;

namespace MeetingRecorder;

public partial class App : Application
{
    private static Mutex? _mutex;
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
        const string appName = "MeetingRecorderUniqueMutexName_f197da08";
        _mutex = new Mutex(true, appName, out bool createdNew);

        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Meeting Recorder is already running.",
                "Meeting Recorder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ConfigureServices();

        var settings = _serviceProvider!.GetRequiredService<AppSettings>();
        ApplyUiLanguage(settings.UiLanguage);
        ApplyTheme(settings.Theme);

        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

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

    public static string SettingsFilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingRecorder",
        "settings.json");

    public static string TokenFolderPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingRecorder",
        "token.json");

    private static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
        return new AppSettings();
    }

    internal static void SaveSettings(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        var settings = LoadSettings();
        services.AddSingleton<AppSettings>(settings);
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IFileIOService, FileIOService>();
        services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        services.AddSingleton<IGlobalHotkeyService>(_ => new GlobalHotkeyService(IntPtr.Zero));
        services.AddSingleton<IAudioSessionMonitor, AudioSessionDetector>();
        services.AddSingleton<IAudioRecorder, WasapiRecorder>();
        services.AddSingleton<ICloudSyncService>(sp =>
            new GoogleDriveSyncService(sp.GetRequiredService<AppSettings>()));
        services.AddSingleton<SessionCoordinator>(sp =>
            new SessionCoordinator(
                sp.GetRequiredService<IAudioSessionMonitor>(),
                sp.GetRequiredService<IDateTimeProvider>(),
                TimeSpan.FromSeconds(sp.GetRequiredService<AppSettings>().DebounceSeconds),
                sp.GetRequiredService<AppSettings>(),
                sp.GetRequiredService<IFileIOService>(),
                sp.GetRequiredService<ICloudSyncService>()));

        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }

    private void Show_Click(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
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
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _notifyIcon?.Dispose();
        _serviceProvider?.Dispose();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ObjectDisposedException) { }
        catch (ApplicationException) { } // Mutex not owned by calling thread
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
        {
            var settings = _serviceProvider?.GetService<AppSettings>();
            if (settings != null && settings.Theme == "System")
            {
                Dispatcher.Invoke(() => ApplyTheme("System"));
            }
        }
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 0;
            }
        }
        catch
        {
            // Fallback to light
        }
        return false;
    }

    internal static void ApplyTheme(string themeName)
    {
        string actualTheme = themeName;
        if (actualTheme == "System" || string.IsNullOrEmpty(actualTheme))
        {
            actualTheme = IsSystemDarkTheme() ? "Dark" : "Light";
        }

        var app = (App)Application.Current;
        if (app == null)
        {
            return;
        }
        var existingThemes = app.Resources.MergedDictionaries
            .Where(d => d.Source != null && (d.Source.OriginalString.Contains("LightTheme.xaml") || d.Source.OriginalString.Contains("DarkTheme.xaml")))
            .ToList();

        foreach (var dict in existingThemes)
        {
            app.Resources.MergedDictionaries.Remove(dict);
        }

        var uri = new Uri($"/Themes/{actualTheme}Theme.xaml", UriKind.Relative);
        try
        {
            var dict = new ResourceDictionary { Source = uri };
            app.Resources.MergedDictionaries.Add(dict);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load theme {actualTheme}: {ex.Message}");
        }
    }
}
