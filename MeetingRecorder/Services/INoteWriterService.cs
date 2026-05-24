using System;

namespace MeetingRecorder.Services;

public interface INoteWriterService
{
    string BuildMarkdownNote(DateTime recordingStartTime, DateTime noteTimestamp, string noteText);
}
