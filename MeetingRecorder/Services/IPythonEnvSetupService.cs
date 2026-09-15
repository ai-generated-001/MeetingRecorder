using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public interface IPythonEnvSetupService
{
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
    /// and installs or upgrades notebooklm-py.
    /// </summary>
    Task SetupEnvironmentAsync(IProgress<string> progress, CancellationToken cancellationToken = default);
}
