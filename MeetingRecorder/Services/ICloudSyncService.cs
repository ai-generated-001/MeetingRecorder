using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public interface ICloudSyncService
{
    void EnqueueUpload(string filePath);

    /// <summary>
    /// Raised on the thread-pool when a file permanently fails to upload
    /// (i.e. after all retry attempts are exhausted).
    /// The event argument is a short, user-readable error message.
    /// </summary>
    event EventHandler<string>? UploadFailed;

    /// <summary>
    /// Raised on the thread-pool when a file is successfully uploaded.
    /// The event argument is the absolute path to the uploaded file.
    /// </summary>
    event EventHandler<string>? UploadCompleted;

    /// <summary>
    /// Organizes existing files directly under the user-selected folder in cloud storage
    /// into monthly subfolders (named YYYYMM) based on their creation/upload time.
    /// Returns the number of files moved.
    /// </summary>
    Task<int> OrganizeExistingFilesAsync(CancellationToken cancellationToken);
}
