using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public class OrganizeProgressEventArgs : EventArgs
{
    public bool IsOrganizing { get; }
    public string StatusText { get; }
    public int ProgressValue { get; }

    public OrganizeProgressEventArgs(bool isOrganizing, string statusText, int progressValue)
    {
        IsOrganizing = isOrganizing;
        StatusText = statusText;
        ProgressValue = progressValue;
    }
}

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
    /// Gets whether a file organization task is currently running in the background.
    /// </summary>
    bool IsOrganizing { get; }

    /// <summary>
    /// Gets the current status message of the organization task.
    /// </summary>
    string OrganizeStatusText { get; }

    /// <summary>
    /// Gets the current progress value (0 to 100) of the organization task.
    /// </summary>
    int OrganizeProgressValue { get; }

    /// <summary>
    /// Raised when the progress of the organization task changes.
    /// </summary>
    event EventHandler<OrganizeProgressEventArgs>? OrganizeProgressChanged;

    /// <summary>
    /// Starts the background process to organize existing files.
    /// </summary>
    void StartOrganizeExistingFiles();
}
