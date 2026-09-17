using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public sealed class PythonEnvSetupService : IPythonEnvSetupService, IDisposable
{
    private readonly object _setupLock = new();
    private readonly object _processLock = new();
    private Process? _currentProcess;
    private CancellationTokenSource? _setupCts;
    private Task? _setupTask;

    public bool IsSettingUp { get; private set; }
    public string ProgressText { get; private set; } = "";

    public event EventHandler<PythonEnvSetupProgressEventArgs>? SetupProgressChanged;
    public event EventHandler<PythonEnvSetupCompletedEventArgs>? SetupCompleted;

    public bool StartSetup()
    {
        lock (_setupLock)
        {
            if (IsSettingUp)
            {
                return false;
            }

            IsSettingUp = true;
            ProgressText = "Starting setup...";
            SetupProgressChanged?.Invoke(this, new PythonEnvSetupProgressEventArgs(ProgressText));

            _setupCts = new CancellationTokenSource();
            var token = _setupCts.Token;

            var progress = new Progress<string>(msg =>
            {
                ProgressText = msg;
                SetupProgressChanged?.Invoke(this, new PythonEnvSetupProgressEventArgs(msg));
            });

            _setupTask = Task.Run(async () =>
            {
                try
                {
                    await SetupEnvironmentAsync(progress, token);
                    lock (_setupLock)
                    {
                        IsSettingUp = false;
                    }
                    SetupCompleted?.Invoke(this, new PythonEnvSetupCompletedEventArgs(true));
                }
                catch (OperationCanceledException)
                {
                    lock (_setupLock)
                    {
                        IsSettingUp = false;
                        ProgressText = "Setup cancelled.";
                    }
                    SetupCompleted?.Invoke(this, new PythonEnvSetupCompletedEventArgs(false, "Setup was cancelled."));
                }
                catch (Exception ex)
                {
                    lock (_setupLock)
                    {
                        IsSettingUp = false;
                        ProgressText = $"Error: {ex.Message}";
                    }
                    SetupCompleted?.Invoke(this, new PythonEnvSetupCompletedEventArgs(false, ex.Message));
                }
            });

            return true;
        }
    }

    public void Cancel()
    {
        lock (_setupLock)
        {
            if (!IsSettingUp) return;
            try
            {
                _setupCts?.Cancel();
            }
            catch { }

            KillCurrentProcess();
        }
    }

    private void KillCurrentProcess()
    {
        lock (_processLock)
        {
            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            finally
            {
                _currentProcess = null;
            }
        }
    }

    public void Dispose()
    {
        Cancel();
        _setupCts?.Dispose();
    }

    public static string GetVenvScriptsDirectory()
    {
        var basePath = App.PythonEnvFolderPath;
        return OperatingSystem.IsWindows()
            ? Path.Combine(basePath, "Scripts")
            : Path.Combine(basePath, "bin");
    }

    public static string GetVenvPythonExecutable()
    {
        var scriptsDir = GetVenvScriptsDirectory();
        return OperatingSystem.IsWindows()
            ? Path.Combine(scriptsDir, "python.exe")
            : Path.Combine(scriptsDir, "python");
    }

    public string? GetVenvNotebookLmExecutable()
    {
        var scriptsDir = GetVenvScriptsDirectory();
        var candidate = OperatingSystem.IsWindows()
            ? Path.Combine(scriptsDir, "notebooklm.exe")
            : Path.Combine(scriptsDir, "notebooklm");

        return File.Exists(candidate) ? candidate : null;
    }

    public bool IsVenvReady()
    {
        return !string.IsNullOrWhiteSpace(GetVenvNotebookLmExecutable());
    }

    public async Task<string?> FindHostPythonExecutableAsync(CancellationToken cancellationToken = default)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "py", "python", "python3" }
            : new[] { "python3", "python" };

        foreach (var candidate in candidates)
        {
            if (await TestPythonCandidateAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<bool> TestPythonCandidateAsync(string executable, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var timeoutTask = Task.Delay(3000, cancellationToken);
            var waitTask = process.WaitForExitAsync(cancellationToken);

            var completedTask = await Task.WhenAny(waitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                try { process.Kill(); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task SetupEnvironmentAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report("Detecting host Python runtime...");

        var hostPython = await FindHostPythonExecutableAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(hostPython))
        {
            throw new InvalidOperationException(
                "Python 3.10+ was not found on your system. Please install Python from https://www.python.org/downloads/ or the Microsoft Store and ensure it is added to your PATH.");
        }

        var venvDir = App.PythonEnvFolderPath;
        var venvPython = GetVenvPythonExecutable();

        if (!File.Exists(venvPython))
        {
            progress.Report($"Creating isolated virtual environment in: {venvDir}...");
            if (Directory.Exists(venvDir))
            {
                try { Directory.Delete(venvDir, recursive: true); } catch { }
            }

            var (exitCode, output) = await RunCommandAsync(hostPython, $"-m venv \"{venvDir}\"", progress, cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Failed to create Python virtual environment (exit code {exitCode}): {output}");
            }
        }

        if (!File.Exists(venvPython))
        {
            throw new FileNotFoundException($"Virtual environment Python executable not found at: {venvPython}");
        }

        progress.Report("Upgrading pip inside virtual environment...");
        await RunCommandAsync(venvPython, "-m pip install --upgrade pip", progress, cancellationToken);

        progress.Report("Installing / upgrading notebooklm-py package...");
        var (pkgExitCode, pkgOutput) = await RunCommandAsync(venvPython, "-m pip install --upgrade notebooklm-py", progress, cancellationToken);
        if (pkgExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to install notebooklm-py (exit code {pkgExitCode}): {pkgOutput}");
        }

        var notebookLmExe = GetVenvNotebookLmExecutable();
        if (string.IsNullOrWhiteSpace(notebookLmExe))
        {
            throw new FileNotFoundException("notebooklm-py was installed, but the notebooklm CLI executable was not found in the virtual environment.");
        }

        progress.Report($"Environment setup complete! CLI ready: {notebookLmExe}");
    }

    private async Task<(int ExitCode, string Output)> RunCommandAsync(
        string fileName,
        string arguments,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        lock (_processLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _currentProcess = process;
        }

        var sb = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                sb.AppendLine(e.Data);
                progress.Report(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                sb.AppendLine(e.Data);
                progress.Report(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, sb.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            throw;
        }
        finally
        {
            lock (_processLock)
            {
                if (_currentProcess == process)
                {
                    _currentProcess = null;
                }
            }
        }
    }
}
