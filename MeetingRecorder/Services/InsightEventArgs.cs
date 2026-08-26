using System;

namespace MeetingRecorder.Services;

public class InsightEventArgs : EventArgs
{
    public string InsightText { get; }
    public string MentionSnippet { get; }
    public DateTime Timestamp { get; }

    public InsightEventArgs(string insightText, string mentionSnippet, DateTime? timestamp = null)
    {
        InsightText = insightText;
        MentionSnippet = mentionSnippet;
        Timestamp = timestamp ?? DateTime.Now;
    }
}
