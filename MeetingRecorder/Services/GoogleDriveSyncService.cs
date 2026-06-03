using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Collections.Generic;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public sealed class GoogleDriveSyncService : ICloudSyncService, IDisposable
{
    private readonly AppSettings _settings;
    private readonly Channel<string> _uploadChannel;
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _authSemaphore = new(1, 1);
    private DriveService? _driveService;
    private string? _targetFolderId;

    public GoogleDriveSyncService(AppSettings settings)
    {
        _settings = settings;
        _uploadChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();

        // Start processing queue in background
        _ = Task.Run(() => ProcessQueueAsync(_cts.Token));
    }

    /// <summary>
    /// Clears the cached DriveService so the next upload re-authenticates.
    /// Call this after the user changes their OAuth credentials.
    /// </summary>
    public void ResetCredentials()
    {
        _authSemaphore.Wait();
        try
        {
            _driveService?.Dispose();
            _driveService = null;
            _targetFolderId = null;
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    public void EnqueueUpload(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        _uploadChannel.Writer.TryWrite(filePath);
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (await _uploadChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_uploadChannel.Reader.TryRead(out var filePath))
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    if (!File.Exists(filePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"File not found, skipping sync: {filePath}");
                        continue;
                    }

                    await UploadFileWithRetryAsync(filePath, cancellationToken);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to upload file '{filePath}': {ex}");
                }
            }
        }
    }

    private async Task UploadFileWithRetryAsync(string filePath, CancellationToken cancellationToken)
    {
        int maxAttempts = 3;
        int delayMs = 2000;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var service = await GetDriveServiceAsync(cancellationToken);
                var folderId = await GetOrCreateFolderIdAsync(service, cancellationToken);

                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = Path.GetFileName(filePath),
                    Parents = new List<string> { folderId }
                };

                string mimeType = Path.GetExtension(filePath).ToLowerInvariant() switch
                {
                    ".mp3" => "audio/mpeg",
                    ".wav" => "audio/wav",
                    ".md" => "text/markdown",
                    _ => "application/octet-stream"
                };

                using (var uploadStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var uploadRequest = service.Files.Create(fileMetadata, uploadStream, mimeType);
                    uploadRequest.Fields = "id, name, webViewLink";
                    
                    var progress = await uploadRequest.UploadAsync(cancellationToken);
                    if (progress.Status == Google.Apis.Upload.UploadStatus.Failed)
                    {
                        throw progress.Exception ?? new InvalidOperationException($"Upload failed with status {progress.Status}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Successfully uploaded to Google Drive: {filePath}");
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Attempt {attempt} failed to upload '{filePath}': {ex.Message}");
                if (attempt == maxAttempts)
                {
                    throw;
                }
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }
    }

    private async Task<DriveService> GetDriveServiceAsync(CancellationToken cancellationToken)
    {
        if (_driveService != null) return _driveService;

        await _authSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_driveService != null) return _driveService;

            ClientSecrets secrets = ResolveClientSecrets();

            var tokenFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "token.json");
            var dpapiStore = new DpapiFileDataStore(tokenFolderPath);

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                new[] { DriveService.Scope.DriveFile },
                "user",
                cancellationToken,
                dpapiStore);

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "MeetingRecorder"
            });

            return _driveService;
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    private ClientSecrets ResolveClientSecrets()
    {
        // Prefer user-supplied BYOK credentials.
        if (!string.IsNullOrWhiteSpace(_settings.GoogleClientId) &&
            !string.IsNullOrWhiteSpace(_settings.GoogleClientSecret))
        {
            return new ClientSecrets
            {
                ClientId = _settings.GoogleClientId,
                ClientSecret = _settings.GoogleClientSecret
            };
        }

        // Fall back to the embedded credentials.json.
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("MeetingRecorder.credentials.json");
        if (stream == null)
        {
            throw new InvalidOperationException(
                "No Google OAuth credentials found. " +
                "Please supply a Client ID and Client Secret in Settings, " +
                "or embed a credentials.json file in the project.");
        }
        return GoogleClientSecrets.FromStream(stream).Secrets;
    }

    private async Task<string> GetOrCreateFolderIdAsync(DriveService service, CancellationToken cancellationToken)
    {
        if (_targetFolderId != null) return _targetFolderId;

        var listRequest = service.Files.List();
        listRequest.Q = "mimeType = 'application/vnd.google-apps.folder' and name = 'Meeting_Auto_Sync' and trashed = false";
        listRequest.Fields = "files(id, name)";

        var response = await listRequest.ExecuteAsync(cancellationToken);
        var folder = response.Files.FirstOrDefault();

        if (folder != null)
        {
            _targetFolderId = folder.Id;
        }
        else
        {
            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = "Meeting_Auto_Sync",
                MimeType = "application/vnd.google-apps.folder"
            };

            var createRequest = service.Files.Create(folderMetadata);
            createRequest.Fields = "id";
            var createdFolder = await createRequest.ExecuteAsync(cancellationToken);
            _targetFolderId = createdFolder.Id;
        }

        return _targetFolderId;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _driveService?.Dispose();
        _authSemaphore.Dispose();
    }
}
