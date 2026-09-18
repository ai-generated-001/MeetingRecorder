using System;
using System.Collections.Generic;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public interface IAudioDeviceService : IDisposable
{
    event EventHandler? DevicesChanged;
    event EventHandler? DefaultDeviceChanged;

    IReadOnlyList<AudioDeviceItem> GetAvailableMicrophones();
    string GetDefaultMicrophoneName();
    string? GetDefaultMicrophoneId();
}
