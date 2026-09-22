using System;
using System.Collections.ObjectModel;

namespace MeetingRecorder.Services;

/// <summary>
/// Severity level for a log entry.
/// </summary>
public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// A single timestamped log entry shown in the activity log panel.
/// </summary>
public sealed class LogEntry
{
    public DateTime Timestamp { get; }
    public LogLevel Level { get; }
    public string Message { get; }

    /// <summary>Emoji icon derived from <see cref="Level"/>.</summary>
    public string Icon => Level switch
    {
        LogLevel.Success => "✅",
        LogLevel.Warning => "⚠️",
        LogLevel.Error   => "❌",
        _                => "ℹ️"
    };

    public string TimestampText => Timestamp.ToString("HH:mm:ss");

    public LogEntry(LogLevel level, string message)
    {
        Timestamp = DateTime.Now;
        Level = level;
        Message = message;
    }
}

/// <summary>
/// Thread-safe singleton logger that maintains a rolling in-memory buffer of
/// <see cref="MaxEntries"/> <see cref="LogEntry"/> objects for display in the
/// activity log panel.
/// </summary>
public sealed class AppLogger
{
    /// <summary>Maximum number of entries kept in <see cref="Entries"/>.</summary>
    public const int MaxEntries = 100;

    /// <summary>
    /// The live collection of log entries.  Always mutated on the UI thread so
    /// that WPF bindings work without a dispatcher wrapper.
    /// </summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>
    /// Append a new log entry.  Safe to call from any thread; the entry is
    /// marshalled to the UI thread automatically.
    /// </summary>
    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(level, message);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => AddEntry(entry));
        }
        else
        {
            AddEntry(entry);
        }
    }

    private void AddEntry(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }
}
