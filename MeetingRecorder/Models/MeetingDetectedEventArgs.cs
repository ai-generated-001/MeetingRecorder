using System;

namespace MeetingRecorder.Models;

public sealed class MeetingDetectedEventArgs : EventArgs
{
    public string ProcessName { get; }
    public string? WindowTitle { get; }

    public MeetingDetectedEventArgs(string processName, string? windowTitle)
    {
        ProcessName = processName;
        WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle;
    }
}
