using System;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public class TranscriptionSegmentEventArgs : EventArgs
{
    public TranscriptionSegment Segment { get; }

    public TranscriptionSegmentEventArgs(TranscriptionSegment segment)
    {
        Segment = segment;
    }
}
