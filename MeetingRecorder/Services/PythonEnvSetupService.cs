using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public sealed class PythonEnvSetupService : IPythonEnvSetupService
{
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
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task SetupEnvironmentAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        progress.Report("Detecting host Python runtime...");

        var hostPython = await FindHostPythonExecutableAsync(cancellationToken);
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

    private static async Task<(int ExitCode, string Output)> RunCommandAsync(
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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, sb.ToString().Trim());
    }
}
