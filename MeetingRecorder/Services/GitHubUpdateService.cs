using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public class GitHubUpdateService : IUpdateService
{
    private static readonly HttpClient _httpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MeetingRecorder", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        return client;
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        // To test the update flow, uncomment this block:
        /*
        return new UpdateInfo(
            "v9.9.9",
            "### Meeting Recorder v9.9.9\n\n* **Auto-update feature** implemented end-to-end.\n* **Light/Dark theme** support.\n* **Cancelable** updates.",
            "https://github.com/ai-generated-001/MeetingRecorder/releases/download/v0.1.6/MeetingRecorder-win-x64-v0.1.6.zip",
            "https://github.com/ai-generated-001/MeetingRecorder/releases/tag/v0.1.6"
        );
        */

        try
        {
            const string url = "https://api.github.com/repos/ai-generated-001/MeetingRecorder/releases/latest";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"GitHub releases API returned: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return null;
            }

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion == null)
            {
                return null;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null)
            {
                currentVersion = new Version(1, 0, 0, 0);
            }

            if (CompareVersions(latestVersion, currentVersion) > 0)
            {
                // Find ZIP asset
                string downloadUrl = "";
                foreach (var asset in release.Assets)
                {
                    if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.BrowserDownloadUrl;
                        break;
                    }
                }

                // If no zip asset, fallback to release HTML URL
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    downloadUrl = release.HtmlUrl;
                }

                return new UpdateInfo(
                    release.TagName,
                    release.Body,
                    downloadUrl,
                    release.HtmlUrl
                );
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking for updates: {ex.Message}");
        }

        return null;
    }

    public async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string tempZipPath = Path.Combine(Path.GetTempPath(), $"MeetingRecorderUpdate_{Guid.NewGuid():N}.zip");
        string tempExtractDir = Path.Combine(Path.GetTempPath(), $"MeetingRecorderExtract_{Guid.NewGuid():N}");

        try
        {
            // 1. Download ZIP
            using (var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var totalRead = 0L;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    if (totalBytes != -1)
                    {
                        double percentage = (double)totalRead / totalBytes;
                        progress?.Report(percentage);
                    }
                }
            }

            // 2. Extract ZIP
            Directory.CreateDirectory(tempExtractDir);
            ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir, true);

            // 3. Prepare replacement batch script
            string currentExe = Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.Location ?? "";
            if (string.IsNullOrEmpty(currentExe))
            {
                throw new Exception("Could not resolve current executable path.");
            }
            string targetDir = Path.GetDirectoryName(currentExe)!;
            string currentPid = Environment.ProcessId.ToString();

            string batchPath = Path.Combine(Path.GetTempPath(), $"install_meeting_recorder_update_{Guid.NewGuid():N}.bat");

            string batchContent = $@"@echo off
set ""pid={currentPid}""
set ""src={tempExtractDir}""
set ""dst={targetDir}""

:wait_loop
tasklist /fi ""PID eq %pid%"" 2>NUL | find /I ""%pid%"" >NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto wait_loop
)

xcopy ""%src%\*"" ""%dst%\"" /y /e /s /q >nul

start """" ""%dst%\MeetingRecorder.exe""

(goto) 2>nul & rd /s /q ""%src%"" & del ""%~f0""
exit
";

            await File.WriteAllTextAsync(batchPath, batchContent, System.Text.Encoding.Default, cancellationToken);

            // 4. Check if administrative privileges are needed
            bool needsAdmin = false;
            try
            {
                string testPath = Path.Combine(targetDir, $"write_test_{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(testPath, "test", cancellationToken);
                File.Delete(testPath);
            }
            catch
            {
                needsAdmin = true;
            }

            // 5. Execute script
            var startInfo = new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (needsAdmin)
            {
                startInfo.Verb = "runas";
            }

            Process.Start(startInfo);

            // 6. Terminate app
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Application.Current.Shutdown();
            });
        }
        catch (Exception ex)
        {
            // Cleanup on failure
            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { /* ignore */ }
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { /* ignore */ }
            throw new Exception($"Failed to install update: {ex.Message}", ex);
        }
    }

    internal static Version? ParseVersion(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;

        string cleanTag = tag.Trim();
        if (cleanTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleanTag = cleanTag.Substring(1);
        }

        int dashIndex = cleanTag.IndexOf('-');
        if (dashIndex > 0)
        {
            cleanTag = cleanTag.Substring(0, dashIndex);
        }

        if (Version.TryParse(cleanTag, out var version))
        {
            return version;
        }
        return null;
    }

    internal static int CompareVersions(Version v1, Version v2)
    {
        int v1Major = v1.Major;
        int v1Minor = v1.Minor;
        int v1Build = v1.Build >= 0 ? v1.Build : 0;
        int v1Revision = v1.Revision >= 0 ? v1.Revision : 0;

        int v2Major = v2.Major;
        int v2Minor = v2.Minor;
        int v2Build = v2.Build >= 0 ? v2.Build : 0;
        int v2Revision = v2.Revision >= 0 ? v2.Revision : 0;

        var norm1 = new Version(v1Major, v1Minor, v1Build, v1Revision);
        var norm2 = new Version(v2Major, v2Minor, v2Build, v2Revision);

        return norm1.CompareTo(norm2);
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
