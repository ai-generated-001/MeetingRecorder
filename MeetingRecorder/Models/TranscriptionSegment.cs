using System;

namespace MeetingRecorder.Models;

public record TranscriptionSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text
);
