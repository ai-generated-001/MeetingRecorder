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
    private bool _disposed;

    public event EventHandler<string>? UploadFailed;
    public event EventHandler<string>? UploadCompleted;
    public event EventHandler<string>? UploadEnqueued;

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
        UploadEnqueued?.Invoke(this, filePath);
    }

    public void StartOrganizeExistingFiles()
    {
        // NotebookLM does not organize historical files like Drive
    }

    /// <summary>
    /// Checks whether Google OAuth credentials (Client ID and Secret) exist.
    /// Retained for backward compatibility.
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
    /// Retained for backward compatibility.
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
    /// Gets the path to the NotebookLM storage state file.
    /// </summary>
    public static string GetDefaultStorageStatePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".notebooklm",
            "profiles",
            "default",
            "storage_state.json");
    }

    /// <summary>
    /// Checks whether the user is authenticated with NotebookLM (valid storage_state.json exists).
    /// </summary>
    public static bool IsNotebookLmAuthenticated()
    {
        var defaultPath = GetDefaultStorageStatePath();
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".notebooklm",
            "storage_state.json");
        try
        {
            return (File.Exists(defaultPath) && new FileInfo(defaultPath).Length > 0) ||
                   (File.Exists(legacyPath) && new FileInfo(legacyPath).Length > 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether Google Drive user token exists.
    /// </summary>
    public static bool IsGoogleUserAuthenticated()
    {
        var tokenFolderPath = App.TokenFolderPath;
        var tokenFilePath = Path.Combine(tokenFolderPath, "dpapi_user.dat");
        return File.Exists(tokenFilePath);
    }

    /// <summary>
    /// Launches interactive notebooklm login.
    /// </summary>
    public static async Task<bool> RunLoginAsync(string? configuredCliPath, CancellationToken cancellationToken = default)
    {
        var cliPath = FindNotebookLmExecutable(configuredCliPath);
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            throw new FileNotFoundException("The notebooklm CLI tool was not found. Please setup the Python environment first.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = "login",
            UseShellExecute = true
        };

        using var process = Process.Start(psi);
        if (process == null) return false;

        await process.WaitForExitAsync(cancellationToken);
        return IsNotebookLmAuthenticated();
    }

    /// <summary>
    /// Logs out of NotebookLM.
    /// </summary>
    public static async Task<bool> RunLogoutAsync(string? configuredCliPath, CancellationToken cancellationToken = default)
    {
        var cliPath = FindNotebookLmExecutable(configuredCliPath);
        if (!string.IsNullOrWhiteSpace(cliPath))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = "auth logout",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
            }
            catch { }
        }

        try
        {
            var defaultPath = GetDefaultStorageStatePath();
            if (File.Exists(defaultPath)) File.Delete(defaultPath);
            var legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".notebooklm",
                "storage_state.json");
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
        catch { }

        return !IsNotebookLmAuthenticated();
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

                // Auth gating: Must be signed in to NotebookLM
                if (!IsNotebookLmAuthenticated())
                {
                    const string authError = "NotebookLM upload skipped: NotebookLM is not authenticated. Please sign in via Settings.";
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

    public static async Task<string> GetOrCreateNotebookIdAsync(string cliPath, string notebookTitle, CancellationToken cancellationToken)
    {
        // 1. Check existing notebooks via notebooklm list --json
        var listResult = await ExecuteCliCommandAsync(cliPath, "list --json", cancellationToken);
        if (listResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(listResult.Output))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(listResult.Output);
                if (doc.RootElement.TryGetProperty("notebooks", out var notebooks) && notebooks.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var nb in notebooks.EnumerateArray())
                    {
                        if (nb.TryGetProperty("title", out var titleProp) &&
                            titleProp.GetString()?.Equals(notebookTitle, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (nb.TryGetProperty("id", out var idProp) && !string.IsNullOrWhiteSpace(idProp.GetString()))
                            {
                                return idProp.GetString()!;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotebookLmSyncService] Error parsing notebook list: {ex.Message}");
            }
        }

        // 2. Create new notebook if not found
        var escapedTitle = notebookTitle.Replace("\"", "\\\"");
        var createResult = await ExecuteCliCommandAsync(cliPath, $"create \"{escapedTitle}\" --json", cancellationToken);
        if (createResult.ExitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(createResult.Error) ? createResult.Output : createResult.Error;
            throw new InvalidOperationException($"Failed to create notebook '{notebookTitle}': {err}");
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(createResult.Output);
            if (doc.RootElement.TryGetProperty("notebook", out var nb) &&
                nb.TryGetProperty("id", out var idProp) &&
                !string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                return idProp.GetString()!;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse created notebook response: {ex.Message}. Output: {createResult.Output}");
        }

        throw new InvalidOperationException($"Could not determine notebook ID for '{notebookTitle}'.");
    }

    private async Task UploadToNotebookLmAsync(string filePath, CancellationToken cancellationToken)
    {
        var cliPath = FindNotebookLmExecutable(_settings.NotebookLmCliPath);
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            throw new FileNotFoundException(
                "The notebooklm CLI tool was not found. Please ensure python and notebooklm-py are set up.");
        }

        var notebookName = ResolveNotebookName(_settings.NotebookLmNotebookPattern, DateTime.Now);
        var notebookId = await GetOrCreateNotebookIdAsync(cliPath, notebookName, cancellationToken);

        var escapedFilePath = filePath.Replace("\"", "\\\"");
        var arguments = $"source add \"{escapedFilePath}\" --notebook \"{notebookId}\"";

        var uploadResult = await ExecuteCliCommandAsync(cliPath, arguments, cancellationToken);
        if (uploadResult.ExitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(uploadResult.Error) ? uploadResult.Output : uploadResult.Error;
            throw new InvalidOperationException($"notebooklm process exited with code {uploadResult.ExitCode}: {err}");
        }

        Debug.WriteLine($"[NotebookLmSyncService] Successfully added source '{filePath}' to notebook '{notebookName}' ({notebookId})");
    }

    private static async Task<(int ExitCode, string Output, string Error)> ExecuteCliCommandAsync(string cliPath, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
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

        return (process.ExitCode, outputBuilder.ToString().Trim(), errorBuilder.ToString().Trim());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException) { }

        _cts.Dispose();
    }
}
