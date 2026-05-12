using System.Collections.Generic;
using System.IO;

namespace MeetingRecorder.Models;

public class AppSettings
{
    public List<string> WhitelistedProcesses { get; set; } = new()
    {
        "wemeetapp", "Zoom", "ms-teams", "Feishu", "DingTalk", "Webex"
    };

    public string OutputDirectory { get; set; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
        "MeetingRecordings");

    public int DebounceSeconds { get; set; } = 5;

    public OutputFormat OutputFormat { get; set; } = OutputFormat.Mp3;
}

public enum OutputFormat
{
    Wav,
    Mp3
}
