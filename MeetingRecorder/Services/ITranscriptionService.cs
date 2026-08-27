using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public interface ITranscriptionService : IDisposable
{
    event EventHandler<TranscriptionSegmentEventArgs> SegmentTranscribed;
    event EventHandler<TranscriptionSegmentEventArgs> PartialSegmentTranscribed;
    event EventHandler<string> StatusChanged;
    
    bool IsTranscribing { get; }
    
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    void StartTranscription();
    void StopTranscription();
    void FeedAudioData(float[] samples, int count);
    List<TranscriptionSegment> GetFullTranscript();
}
