using System;

namespace MeetingRecorder.Services;

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public SessionState OldState { get; }
    public SessionState NewState { get; }

    public SessionStateChangedEventArgs(SessionState oldState, SessionState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}
