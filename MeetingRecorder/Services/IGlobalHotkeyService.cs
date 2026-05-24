using System;

namespace MeetingRecorder.Services;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<int>? HotkeyPressed;

    bool Register(int id, uint modifiers, uint virtualKey);
    void Unregister(int id);
}
