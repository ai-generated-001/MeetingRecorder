using System;

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
}
