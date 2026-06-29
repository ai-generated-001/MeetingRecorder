using System;

namespace MeetingRecorder.Services;

public class UpdateInfo
{
    public string Version { get; }
    public string ReleaseNotes { get; }
    public string DownloadUrl { get; }
    public string ReleaseUrl { get; }

    public UpdateInfo(string version, string releaseNotes, string downloadUrl, string releaseUrl)
    {
        Version = version;
        ReleaseNotes = releaseNotes;
        DownloadUrl = downloadUrl;
        ReleaseUrl = releaseUrl;
    }
}
