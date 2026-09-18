using System;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }
    event EventHandler<AudioDataEventArgs> AudioDataAvailable;
    event EventHandler<string>? MicrophoneWarning;
    event EventHandler? MicrophoneRestored;
    void Start(string filePath, OutputFormat format = OutputFormat.Mp3);
    void Stop();
    bool SwitchMicrophone(string? deviceId);
}
