using System;

namespace MeetingRecorder.Services;

public class AudioDataEventArgs : EventArgs
{
    public float[] Buffer { get; }
    public int Count { get; }

    public AudioDataEventArgs(float[] buffer, int count)
    {
        Buffer = buffer;
        Count = count;
    }
}
