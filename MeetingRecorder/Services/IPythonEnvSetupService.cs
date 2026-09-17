using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public class PythonEnvSetupProgressEventArgs : EventArgs
{
    public string ProgressText { get; }

    public PythonEnvSetupProgressEventArgs(string progressText)
    {
        ProgressText = progressText;
    }
}

public class PythonEnvSetupCompletedEventArgs : EventArgs
{
    public bool Success { get; }
    public string? ErrorMessage { get; }

    public PythonEnvSetupCompletedEventArgs(bool success, string? errorMessage = null)
    {
        Success = success;
        ErrorMessage = errorMessage;
    }
}

public interface IPythonEnvSetupService
{
    /// <summary>
    /// Gets whether the Python environment setup is currently in progress.
    /// </summary>
    bool IsSettingUp { get; }

    /// <summary>
    /// Gets the latest progress or status text from the setup process.
    /// </summary>
    string ProgressText { get; }

    /// <summary>
    /// Raised when setup progress or log output changes.
    /// </summary>
    event EventHandler<PythonEnvSetupProgressEventArgs>? SetupProgressChanged;

    /// <summary>
    /// Raised when the setup task completes, succeeds, or fails.
    /// </summary>
    event EventHandler<PythonEnvSetupCompletedEventArgs>? SetupCompleted;

    /// <summary>
    /// Finds an available base Python executable or launcher on the host (e.g., py.exe or python.exe).
    /// </summary>
    Task<string?> FindHostPythonExecutableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the path to the notebooklm.exe binary in the managed virtual environment if it exists.
    /// </summary>
    string? GetVenvNotebookLmExecutable();

    /// <summary>
    /// Returns true if the managed virtual environment exists and has notebooklm installed.
    /// </summary>
    bool IsVenvReady();

    /// <summary>
    /// Creates or updates the isolated virtual environment under App.PythonEnvFolderPath
    /// and installs or upgrades notebooklm-py synchronously.
    /// </summary>
    Task SetupEnvironmentAsync(IProgress<string> progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the background environment setup task if one is not already running.
    /// </summary>
    /// <returns>True if the setup was newly started; false if it was already running.</returns>
    bool StartSetup();

    /// <summary>
    /// Cancels any currently running setup process and terminates child processes.
    /// </summary>
    void Cancel();
}
