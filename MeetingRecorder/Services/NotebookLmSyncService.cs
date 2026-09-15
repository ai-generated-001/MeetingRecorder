using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public sealed class NotebookLmSyncService : ICloudSyncService, IDisposable
{
    private readonly AppSettings _settings;
    private readonly Channel<string> _uploadChannel;
    private readonly CancellationTokenSource _cts;

    public event EventHandler<string>? UploadFailed;
    public event EventHandler<string>? UploadCompleted;

    public bool IsOrganizing => false;
    public string OrganizeStatusText => string.Empty;
    public int OrganizeProgressValue => 0;
    public event EventHandler<OrganizeProgressEventArgs>? OrganizeProgressChanged
    {
        add { }
        remove { }
    }

    public NotebookLmSyncService(AppSettings settings)
    {
        _settings = settings;
        _uploadChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();

        _ = Task.Run(() => ProcessQueueAsync(_cts.Token));
    }

    public void EnqueueUpload(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        _uploadChannel.Writer.TryWrite(filePath);
    }

    public void StartOrganizeExistingFiles()
    {
        // NotebookLM does not organize historical files like Drive
    }

    /// <summary>
    /// Checks whether Google OAuth credentials (Client ID and Secret) exist,
    /// either from custom user settings or build-time injected assembly metadata.
    /// </summary>
    public static bool HasGoogleOAuthCredentials(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.GoogleClientId) &&
            !string.IsNullOrWhiteSpace(settings.GoogleClientSecret))
        {
            return true;
        }

        var assembly = Assembly.GetExecutingAssembly();
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var injectedClientId = metadata.FirstOrDefault(a => a.Key == "GoogleClientId")?.Value;
        var injectedClientSecret = metadata.FirstOrDefault(a => a.Key == "GoogleClientSecret")?.Value;

        return !string.IsNullOrWhiteSpace(injectedClientId) && !string.IsNullOrWhiteSpace(injectedClientSecret);
    }

    /// <summary>
    /// Resolves Google Client ID and Secret if available.
    /// </summary>
    public static (string? ClientId, string? ClientSecret) ResolveGoogleCredentials(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.GoogleClientId) &&
            !string.IsNullOrWhiteSpace(settings.GoogleClientSecret))
        {
            return (settings.GoogleClientId.Trim(), settings.GoogleClientSecret.Trim());
        }

        var assembly = Assembly.GetExecutingAssembly();
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var injectedClientId = metadata.FirstOrDefault(a => a.Key == "GoogleClientId")?.Value;
        var injectedClientSecret = metadata.FirstOrDefault(a => a.Key == "GoogleClientSecret")?.Value;

        if (!string.IsNullOrWhiteSpace(injectedClientId) && !string.IsNullOrWhiteSpace(injectedClientSecret))
        {
            return (injectedClientId.Trim(), injectedClientSecret.Trim());
        }

        return (null, null);
    }

    /// <summary>
    /// Checks whether the user is currently authenticated with Google (i.e. valid saved token file exists).
    /// </summary>
    public static bool IsGoogleUserAuthenticated()
    {
        var tokenFolderPath = App.TokenFolderPath;
        var tokenFilePath = Path.Combine(tokenFolderPath, "dpapi_user.dat");
        return File.Exists(tokenFilePath);
    }

    /// <summary>
    /// Resolves the notebook name using the configured pattern and current date/time.
    /// </summary>
    public static string ResolveNotebookName(string pattern, DateTime dateTime)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = "Meetings {Year}-{Month}";
        }

        return pattern
            .Replace("{Year}", dateTime.ToString("yyyy"))
            .Replace("{Month}", dateTime.ToString("MM"))
            .Replace("{Day}", dateTime.ToString("dd"))
            .Trim();
    }

    /// <summary>
    /// Resolves the executable path to notebooklm.
    /// </summary>
    public static string? FindNotebookLmExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        // Check managed virtual environment first
        var venvScriptsDir = OperatingSystem.IsWindows()
            ? Path.Combine(App.PythonEnvFolderPath, "Scripts")
            : Path.Combine(App.PythonEnvFolderPath, "bin");
        var venvCandidate = OperatingSystem.IsWindows()
            ? Path.Combine(venvScriptsDir, "notebooklm.exe")
            : Path.Combine(venvScriptsDir, "notebooklm");

        if (File.Exists(venvCandidate))
        {
            return venvCandidate;
        }

        // Search PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var dir in paths)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, "notebooklm" + ext);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (await _uploadChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_uploadChannel.Reader.TryRead(out var filePath))
            {
                if (cancellationToken.IsCancellationRequested) return;

                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"[NotebookLmSyncService] File not found: {filePath}");
                    continue;
                }

                // Auth gating: Must have credentials and be signed in
                if (!HasGoogleOAuthCredentials(_settings) || !IsGoogleUserAuthenticated())
                {
                    const string authError = "NotebookLM upload skipped: Google authentication is not configured or signed in.";
                    Debug.WriteLine($"[NotebookLmSyncService] {authError}");
                    UploadFailed?.Invoke(this, authError);
                    continue;
                }

                try
                {
                    await UploadToNotebookLmAsync(filePath, cancellationToken);
                    UploadCompleted?.Invoke(this, filePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NotebookLmSyncService] Upload failed for '{filePath}': {ex.Message}");
                    UploadFailed?.Invoke(this, ex.Message);
                }
            }
        }
    }

    private async Task UploadToNotebookLmAsync(string filePath, CancellationToken cancellationToken)
    {
        var cliPath = FindNotebookLmExecutable(_settings.NotebookLmCliPath);
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            throw new FileNotFoundException(
                "The notebooklm CLI tool was not found on PATH. Please ensure python and notebooklm-py are installed (pip install notebooklm-py).");
        }

        var notebookName = ResolveNotebookName(_settings.NotebookLmNotebookPattern, DateTime.Now);
        var (clientId, clientSecret) = ResolveGoogleCredentials(_settings);

        var arguments = new List<string>
        {
            "source",
            "add-file",
            $"\"{filePath}\"",
            "--notebook",
            $"\"{notebookName}\"",
            "--create-notebook-if-missing"
        };

        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
        {
            arguments.Add("--client-id");
            arguments.Add($"\"{clientId}\"");
            arguments.Add("--client-secret");
            arguments.Add($"\"{clientSecret}\"");
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = string.Join(" ", arguments),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) outputBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var err = errorBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(err))
            {
                err = outputBuilder.ToString().Trim();
            }

            throw new InvalidOperationException(
                $"notebooklm process exited with code {process.ExitCode}: {err}");
        }

        Debug.WriteLine($"[NotebookLmSyncService] Successfully added source to notebook '{notebookName}': {outputBuilder.ToString().Trim()}");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
