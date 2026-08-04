using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public interface ITranscriptionService : IDisposable
{
    event EventHandler<TranscriptionSegmentEventArgs> SegmentTranscribed;
    event EventHandler<string> StatusChanged;
    
    bool IsTranscribing { get; }
    
    Task InitializeModelAsync(string modelSize, string language = "auto");
    Task DownloadModelAsync(string modelSize, string language = "auto");
    void StartTranscription();
    void StopTranscription();
    void FeedAudioData(float[] samples, int count);
    List<TranscriptionSegment> GetFullTranscript();
}
