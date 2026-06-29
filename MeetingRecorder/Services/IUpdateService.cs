using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken);
    Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress, CancellationToken cancellationToken);
}
