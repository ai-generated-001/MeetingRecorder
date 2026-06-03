using System.Collections.Generic;
using System.IO;

namespace MeetingRecorder.Models;

public class AppSettings
{
    public List<string> WhitelistedProcesses { get; set; } = new()
    {
        "wemeetapp", "Zoom", "ms-teams", "ms-teams_modulehost", "Teams", "Feishu", "DingTalk", "Webex"
    };

    public string OutputDirectory { get; set; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
        "MeetingRecordings");

    public int DebounceSeconds { get; set; } = 5;

    public OutputFormat OutputFormat { get; set; } = OutputFormat.Mp3;

    public string UiLanguage { get; set; } = "";

    /// <summary>
    /// When false, recording files are not uploaded to Google Drive.
    /// Defaults to false so sync must be explicitly opted in.
    /// </summary>
    public bool GoogleDriveEnabled { get; set; } = false;

    /// <summary>
    /// Optional user-supplied OAuth 2.0 Client ID ("Bring Your Own Key").
    /// When set, overrides the build-time injected credentials.
    /// </summary>
    public string GoogleClientId { get; set; } = "";

    /// <summary>
    /// Optional user-supplied OAuth 2.0 Client Secret ("Bring Your Own Key").
    /// When set, overrides the build-time injected credentials.
    /// </summary>
    public string GoogleClientSecret { get; set; } = "";

    /// <summary>
    /// Target folder path inside Google Drive where recordings are uploaded.
    /// Use forward-slashes for nested folders, e.g. "Work/Meetings".
    /// Defaults to "Meeting_Auto_Sync" (root-level folder).
    /// </summary>
    public string GoogleDriveFolderPath { get; set; } = "Meeting_Auto_Sync";
}

public enum OutputFormat
{
    Wav,
    Mp3
}
