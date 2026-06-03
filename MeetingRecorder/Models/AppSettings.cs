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
    /// Optional user-supplied OAuth 2.0 Client ID ("Bring Your Own Key").
    /// When set, overrides the embedded credentials.json.
    /// </summary>
    public string GoogleClientId { get; set; } = "";

    /// <summary>
    /// Optional user-supplied OAuth 2.0 Client Secret ("Bring Your Own Key").
    /// When set, overrides the embedded credentials.json.
    /// </summary>
    public string GoogleClientSecret { get; set; } = "";
}

public enum OutputFormat
{
    Wav,
    Mp3
}
