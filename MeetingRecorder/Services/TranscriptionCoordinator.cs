using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

/// <summary>
/// Handles real-time transcription session coordination, mention detection, and transcript saving.
/// </summary>
public class TranscriptionCoordinator : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IInsightService _insightService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICloudSyncService _cloudSyncService;

    private readonly List<TranscriptionSegment> _recentSegments = new();
    private readonly object _segmentsLock = new();
    private DateTime _lastMentionInsightTime = DateTime.MinValue;

    public TranscriptionCoordinator(
        AppSettings settings,
        ITranscriptionService transcriptionService,
        IInsightService insightService,
        IDateTimeProvider dateTimeProvider,
        ICloudSyncService cloudSyncService)
    {
        _settings = settings;
        _transcriptionService = transcriptionService;
        _insightService = insightService;
        _dateTimeProvider = dateTimeProvider;
        _cloudSyncService = cloudSyncService;

        _transcriptionService.SegmentTranscribed += OnSegmentTranscribed;
    }

    public void FeedAudio(float[] buffer, int count)
    {
        if (_settings.TranscriptionEnabled && _transcriptionService.IsTranscribing)
        {
            _transcriptionService.FeedAudioData(buffer, count);
        }
    }

    public void StartTranscription()
    {
        _transcriptionService.StartTranscription();
    }

    public void StopTranscription()
    {
        _transcriptionService.StopTranscription();
    }

    public void ClearRecentSegments()
    {
        lock (_segmentsLock)
        {
            _recentSegments.Clear();
        }
    }

    public string BuildRecentTranscriptContext(TimeSpan segmentEndTime, int contextSeconds)
    {
        lock (_segmentsLock)
        {
            TimeSpan cutoff = segmentEndTime - TimeSpan.FromSeconds(Math.Max(10, contextSeconds));
            var matching = _recentSegments.Where(s => s.End >= cutoff).ToList();
            if (matching.Count == 0)
            {
                matching = _recentSegments.TakeLast(5).ToList();
            }

            var sb = new System.Text.StringBuilder();
            foreach (var seg in matching)
            {
                sb.AppendLine($"[{seg.Start:hh\\:mm\\:ss}] {seg.Text}");
            }
            return sb.ToString();
        }
    }

    private void OnSegmentTranscribed(object? sender, TranscriptionSegmentEventArgs e)
    {
        lock (_segmentsLock)
        {
            _recentSegments.Add(e.Segment);
            if (_recentSegments.Count > 200)
            {
                _recentSegments.RemoveRange(0, _recentSegments.Count - 200);
            }
        }

        if (!_settings.InsightsEnabled || string.IsNullOrWhiteSpace(_settings.DashScopeApiKey))
        {
            return;
        }

        if (IsUserMentioned(e.Segment.Text, _settings.MentionNames))
        {
            var now = DateTime.UtcNow;
            if ((now - _lastMentionInsightTime).TotalSeconds < 5)
            {
                return;
            }
            _lastMentionInsightTime = now;

            string context = BuildRecentTranscriptContext(e.Segment.End, _settings.InsightContextSeconds);
            _ = _insightService.AnalyzeAsync(e.Segment.Text, context, _settings.TranscriptionLanguage);
        }
    }

    public static bool IsUserMentioned(string text, IEnumerable<string>? mentionNames)
    {
        if (string.IsNullOrWhiteSpace(text) || mentionNames == null) return false;

        foreach (var nameEntry in mentionNames)
        {
            if (string.IsNullOrWhiteSpace(nameEntry)) continue;

            var aliases = nameEntry.Split(new[] { '|', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var alias in aliases)
            {
                string trimmed = alias.Trim();
                if (trimmed.Length == 0) continue;

                if (text.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void Dispose()
    {
        _transcriptionService.SegmentTranscribed -= OnSegmentTranscribed;
    }
}
