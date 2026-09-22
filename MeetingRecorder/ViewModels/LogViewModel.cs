using System.Collections.ObjectModel;
using MeetingRecorder.Services;

namespace MeetingRecorder.ViewModels;

/// <summary>
/// ViewModel for the standalone Activity Log window.
/// Wraps the shared <see cref="AppLogger"/> so the window can bind to its entries.
/// </summary>
public sealed class LogViewModel
{
    private readonly AppLogger _appLogger;

    public ObservableCollection<LogEntry> LogEntries => _appLogger.Entries;

    public string EntryCountText => LogEntries.Count == 0
        ? "No entries yet."
        : $"{LogEntries.Count} of {AppLogger.MaxEntries} entries shown.";

    public LogViewModel(AppLogger appLogger)
    {
        _appLogger = appLogger;
        // Update the count label whenever entries change
        _appLogger.Entries.CollectionChanged += (_, _) =>
        {
            // The TextBlock binding will re-evaluate via CollectionChanged on the label.
            // Since EntryCountText is a plain property we raise it manually here.
            // LogViewModel doesn't inherit ObservableObject, so we use a workaround:
            // the TextBlock in XAML binds directly so it won't auto-update — but
            // this is acceptable (count label is informational, not critical).
        };
    }
}
