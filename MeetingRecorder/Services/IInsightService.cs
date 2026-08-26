using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public interface IInsightService
{
    event EventHandler<InsightEventArgs>? InsightGenerated;
    event EventHandler<string>? StatusChanged;

    Task<string?> AnalyzeAsync(string mentionSnippet, string contextTranscript, string detectedLanguage = "auto", CancellationToken ct = default);
}
