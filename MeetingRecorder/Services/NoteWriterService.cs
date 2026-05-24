using System;
using System.Globalization;

namespace MeetingRecorder.Services;

public sealed class NoteWriterService : INoteWriterService
{
    public string BuildMarkdownNote(DateTime recordingStartTime, DateTime noteTimestamp, string noteText)
    {
        var offset = noteTimestamp - recordingStartTime;
        if (offset < TimeSpan.Zero)
        {
            offset = TimeSpan.Zero;
        }

        var timestamp = $"[{offset:mm\\:ss}]";
        var normalizedText = (noteText ?? string.Empty).Trim();

        return $"- {timestamp} {normalizedText}".TrimEnd();
    }
}
