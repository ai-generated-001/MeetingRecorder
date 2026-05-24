using System;
using FluentAssertions;
using MeetingRecorder.Services;

namespace MeetingRecorder.Tests;

public class NoteWriterServiceTests
{
    [Fact]
    public void BuildMarkdownNote_UsesRelativeTimestampAndTrimmedText()
    {
        var service = new NoteWriterService();
        var recordingStart = new DateTime(2025, 1, 1, 9, 0, 0);
        var noteTime = recordingStart.AddMinutes(1).AddSeconds(5);

        var markdown = service.BuildMarkdownNote(recordingStart, noteTime, "  Follow up with client  ");

        markdown.Should().Be("- [01:05] Follow up with client");
    }

    [Fact]
    public void BuildMarkdownNote_WhenTimestampIsBeforeRecordingStart_ClampsToZero()
    {
        var service = new NoteWriterService();
        var recordingStart = new DateTime(2025, 1, 1, 9, 0, 10);
        var noteTime = recordingStart.AddSeconds(-3);

        var markdown = service.BuildMarkdownNote(recordingStart, noteTime, "Intro note");

        markdown.Should().Be("- [00:00] Intro note");
    }
}
