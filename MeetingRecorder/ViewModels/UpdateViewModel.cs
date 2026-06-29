using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingRecorder.Models;
using MeetingRecorder.Services;

namespace MeetingRecorder.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IUpdateService _updateService;
    private UpdateInfo? _updateInfo;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _currentVersion = "";

    [ObservableProperty]
    private string _latestVersion = "";

    [ObservableProperty]
    private string _releaseNotes = "";

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _downloadStatusText = "";

    [ObservableProperty]
    private string? _errorMessage;

    public event EventHandler? RequestClose;

    // Localized labels
    public string Title => Resources.UpdateAvailableTitle;
    public string HeaderText => Resources.UpdateAvailableHeader;
    public string DescriptionText => Resources.UpdateAvailableDescription;
    public string CurrentVersionLabel => Resources.CurrentVersionLabel;
    public string NewVersionLabel => Resources.NewVersionLabel;
    public string ReleaseNotesLabel => Resources.ReleaseNotesLabel;
    public string UpdateNowText => Resources.UpdateNow;
    public string LaterText => Resources.Later;
    public string SkipVersionText => Resources.SkipVersion;

    public UpdateViewModel(AppSettings settings, IUpdateService updateService)
    {
        _settings = settings;
        _updateService = updateService;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = version?.ToString() ?? "1.0.0.0";
    }

    public void Initialize(UpdateInfo info)
    {
        _updateInfo = info;
        LatestVersion = info.Version;
        ReleaseNotes = info.ReleaseNotes;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteUpdate))]
    private async Task UpdateNowAsync()
    {
        if (_updateInfo == null) return;

        IsDownloading = true;
        ErrorMessage = null;
        DownloadProgress = 0;
        DownloadStatusText = string.Format(Resources.DownloadingUpdate, 0);

        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p * 100;
                DownloadStatusText = string.Format(Resources.DownloadingUpdate, (int)DownloadProgress);
            });

            // Notify UI that commands status changed
            UpdateNowCommand.NotifyCanExecuteChanged();
            SkipVersionCommand.NotifyCanExecuteChanged();
            LaterCommand.NotifyCanExecuteChanged();

            await _updateService.DownloadAndInstallUpdateAsync(_updateInfo, progress, _cts.Token);
            
            // Note: If successful, the app shuts down inside the service.
        }
        catch (OperationCanceledException)
        {
            ResetState();
        }
        catch (Exception ex)
        {
            ResetState();
            ErrorMessage = string.Format(Resources.UpdateFailed, ex.Message);
        }
        finally
        {
            UpdateNowCommand.NotifyCanExecuteChanged();
            SkipVersionCommand.NotifyCanExecuteChanged();
            LaterCommand.NotifyCanExecuteChanged();
        }
    }

    private void ResetState()
    {
        IsDownloading = false;
        DownloadProgress = 0;
        DownloadStatusText = "";
    }

    private bool CanExecuteUpdate() => !IsDownloading;

    [RelayCommand(CanExecute = nameof(CanExecuteSkipOrLater))]
    private void SkipVersion()
    {
        if (_updateInfo != null)
        {
            _settings.SkippedVersion = _updateInfo.Version;
            App.SaveSettings(_settings);
        }
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSkipOrLater))]
    private void Later()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private bool CanExecuteSkipOrLater() => !IsDownloading;

    public void CancelDownload()
    {
        _cts?.Cancel();
        ResetState();
        UpdateNowCommand.NotifyCanExecuteChanged();
        SkipVersionCommand.NotifyCanExecuteChanged();
        LaterCommand.NotifyCanExecuteChanged();
    }
}
