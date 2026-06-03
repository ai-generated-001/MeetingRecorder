using System;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public sealed class RecordingRequestedEventArgs : EventArgs
{
    public MeetingDetectedEventArgs MeetingDetails { get; }
    public string AudioFilePath { get; }

    public RecordingRequestedEventArgs(MeetingDetectedEventArgs meetingDetails, string audioFilePath)
    {
        MeetingDetails = meetingDetails;
        AudioFilePath = audioFilePath;
    }
}
