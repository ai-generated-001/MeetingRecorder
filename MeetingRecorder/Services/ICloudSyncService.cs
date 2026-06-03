namespace MeetingRecorder.Services;

public interface ICloudSyncService
{
    void EnqueueUpload(string filePath);
}
