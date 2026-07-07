using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Collections.Generic;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
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
    private readonly SemaphoreSlim _folderSemaphore = new(1, 1);
    private DriveService? _driveService;
    private string? _targetFolderId;

    /// <inheritdoc />
    public event EventHandler<string>? UploadFailed;

    /// <inheritdoc />
    public event EventHandler<string>? UploadCompleted;

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
                    UploadCompleted?.Invoke(this, filePath);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is expected on app shutdown — do not report as a failure.
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to upload file '{filePath}': {ex}");
                    UploadFailed?.Invoke(this, ex.Message);
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
                var parentFolderId = await GetOrCreateFolderIdAsync(service, cancellationToken);

                DateTime fileTime;
                try
                {
                    fileTime = File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.Now;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to get file write time for '{filePath}': {ex.Message}");
                    fileTime = DateTime.Now;
                }

                string monthFolderName = fileTime.ToString("yyyyMM");
                var (targetFolderId, _) = await GetOrCreateSingleFolderAsync(service, monthFolderName, parentFolderId, cancellationToken);

                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = Path.GetFileName(filePath),
                    Parents = new List<string> { targetFolderId }
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

            var tokenFolderPath = MeetingRecorder.App.TokenFolderPath;
            var dpapiStore = new DpapiFileDataStore(tokenFolderPath);

            // Use non-interactive auth: load the DPAPI-encrypted token that was stored
            // when the user explicitly clicked "Sign in" in Settings. Never open a
            // browser from this background thread — that silently hangs or fails.
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets,
                DataStore = dpapiStore,
                Scopes = new[] { DriveService.Scope.DriveFile }
            });

            var token = await flow.LoadTokenAsync("user", cancellationToken);
            if (token == null)
            {
                throw new InvalidOperationException(
                    "Not signed in to Google Drive. Please sign in from Settings.");
            }

            // Re-use the credential for auto-refresh during Drive API calls.
            // The flow instance is intentionally not disposed here — the UserCredential
            // holds a reference to it and needs it alive for future token refreshes.
            var credential = new UserCredential(flow, "user", token);
            if (credential.Token.IsStale)
            {
                bool refreshed = await credential.RefreshTokenAsync(cancellationToken);
                if (!refreshed)
                {
                    throw new InvalidOperationException(
                        "Google Drive session expired. Please re-sign in from Settings.");
                }
            }

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
        return ResolveClientSecretsInternal(_settings.GoogleClientId, _settings.GoogleClientSecret);
    }

    private ClientSecrets ResolveClientSecretsInternal(string clientId, string clientSecret)
    {
        // Prefer custom user-supplied BYOK credentials if provided.
        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
        {
            return new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };
        }

        // Fall back to the build-time injected client ID and secret.
        var assembly = Assembly.GetExecutingAssembly();
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var injectedClientId = metadata.FirstOrDefault(a => a.Key == "GoogleClientId")?.Value;
        var injectedClientSecret = metadata.FirstOrDefault(a => a.Key == "GoogleClientSecret")?.Value;

        if (!string.IsNullOrWhiteSpace(injectedClientId) && !string.IsNullOrWhiteSpace(injectedClientSecret))
        {
            return new ClientSecrets
            {
                ClientId = injectedClientId,
                ClientSecret = injectedClientSecret
            };
        }

        throw new InvalidOperationException("No Google OAuth credentials found. Please enter your Client ID and Secret in Settings.");
    }

    private async Task<string> GetOrCreateFolderIdAsync(DriveService service, CancellationToken cancellationToken)
    {
        await _folderSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Try the in-memory cache first, then fall back to the persisted ID from settings.
            var candidateId = _targetFolderId
                ?? (string.IsNullOrWhiteSpace(_settings.GoogleDriveFolderId) ? null : _settings.GoogleDriveFolderId);

            if (candidateId != null)
            {
                try
                {
                    var getRequest = service.Files.Get(candidateId);
                    getRequest.Fields = "id, trashed";
                    var folderFile = await getRequest.ExecuteAsync(cancellationToken);
                    if (folderFile != null && folderFile.Trashed != true)
                    {
                        _targetFolderId = candidateId;
                        return _targetFolderId;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cached target folder ID {candidateId} no longer valid or trashed: {ex.Message}");
                }
                _targetFolderId = null;
            }

            // Support nested paths like "Work/Meetings" — walk or create each segment.
            var rawPath = string.IsNullOrWhiteSpace(_settings.GoogleDriveFolderPath)
                ? "Meeting_Auto_Sync"
                : _settings.GoogleDriveFolderPath.Trim();

            var segments = rawPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0) segments = new[] { "Meeting_Auto_Sync" };

            string? parentId = null; // null means root ("My Drive")
            var resolvedSegments = new List<string>();
            foreach (var segment in segments)
            {
                var (folderId, resolvedName) = await GetOrCreateSingleFolderAsync(service, segment, parentId, cancellationToken);
                parentId = folderId;
                resolvedSegments.Add(resolvedName);
            }

            _targetFolderId = parentId!;

            // Persist the resolved folder ID and path so subsequent app restarts
            // can skip the files.list query and avoid creating duplicates.
            bool settingsChanged = false;
            var resolvedPath = string.Join("/", resolvedSegments);
            if (_settings.GoogleDriveFolderPath != resolvedPath)
            {
                _settings.GoogleDriveFolderPath = resolvedPath;
                settingsChanged = true;
            }
            if (_settings.GoogleDriveFolderId != _targetFolderId)
            {
                _settings.GoogleDriveFolderId = _targetFolderId;
                settingsChanged = true;
            }
            if (settingsChanged)
            {
                MeetingRecorder.App.SaveSettings(_settings);
            }

            return _targetFolderId;
        }
        finally
        {
            _folderSemaphore.Release();
        }
    }

    private static string EscapeQueryString(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private async Task<(string FolderId, string FolderName)> GetOrCreateSingleFolderAsync(
        DriveService service,
        string folderName,
        string? parentId,
        CancellationToken cancellationToken)
    {
        var folders = new List<Google.Apis.Drive.v3.Data.File>();
        string? pageToken = null;
        do
        {
            var listRequest = service.Files.List();
            var parentClause = parentId == null
                ? "'root' in parents"
                : $"'{parentId}' in parents";
            listRequest.Q = $"mimeType = 'application/vnd.google-apps.folder' and {parentClause} and name = '{EscapeQueryString(folderName)}' and trashed = false";
            listRequest.Fields = "nextPageToken, files(id, name, createdTime)";
            listRequest.Spaces = "drive";
            // Return oldest first so we consistently pick the original folder
            // if duplicates were previously created.
            listRequest.OrderBy = "createdTime";
            listRequest.PageToken = pageToken;
            listRequest.PageSize = 100;

            var response = await listRequest.ExecuteAsync(cancellationToken);
            if (response.Files != null)
            {
                folders.AddRange(response.Files);
            }
            pageToken = response.NextPageToken;
        } while (pageToken != null);

        // The Drive API query is case-insensitive, so we must do an exact
        // case-sensitive match client-side to avoid picking a wrong folder.
        // Fall back to case-insensitive match if no exact match is found,
        // since the user likely intended the existing folder.
        var existingFolder = folders.FirstOrDefault(f =>
            string.Equals(f.Name, folderName, StringComparison.Ordinal))
            ?? folders.FirstOrDefault(f =>
            string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase));

        if (existingFolder != null)
        {
            return (existingFolder.Id, existingFolder.Name);
        }

        var folderMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = folderName,
            MimeType = "application/vnd.google-apps.folder",
            Parents = parentId == null ? null : new List<string> { parentId }
        };

        var createRequest = service.Files.Create(folderMetadata);
        createRequest.Fields = "id";
        var createdFolder = await createRequest.ExecuteAsync(cancellationToken);
        return (createdFolder.Id, folderName);
    }

    /// <summary>
    /// Checks asynchronously if a token file is stored on disk and, if so, attempts to retrieve the user's email address.
    /// Returns:
    /// - "Not signed in" if no token file exists
    /// - "Signed in as {email}" if a token exists and we retrieve the email
    /// - "Signed in" if a token exists but we fail to retrieve the email (e.g. offline)
    /// </summary>
    public async Task<string> GetAccountStatusStringAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenFolderPath = MeetingRecorder.App.TokenFolderPath;
            var tokenFilePath = Path.Combine(tokenFolderPath, "dpapi_user.dat");
            if (!File.Exists(tokenFilePath))
            {
                return global::MeetingRecorder.Resources.GoogleDriveNotSignedIn;
            }

            // We have a token. Let's try to get client secrets to fetch the email.
            ClientSecrets secrets;
            try
            {
                secrets = ResolveClientSecrets();
            }
            catch
            {
                // If secrets can't be resolved, we can't fetch email, but we have a token.
                return global::MeetingRecorder.Resources.GoogleDriveSignedIn;
            }

            var dpapiStore = new DpapiFileDataStore(tokenFolderPath);
            var initializer = new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets,
                DataStore = dpapiStore,
                Scopes = new[] { DriveService.Scope.DriveFile }
            };

            using var flow = new GoogleAuthorizationCodeFlow(initializer);
            var token = await flow.LoadTokenAsync("user", cancellationToken);
            if (token == null)
            {
                return global::MeetingRecorder.Resources.GoogleDriveNotSignedIn;
            }

            // Attempt to refresh if needed and get email
            var credential = new UserCredential(flow, "user", token);
            if (credential.Token.IsStale)
            {
                bool refreshed = await credential.RefreshTokenAsync(cancellationToken);
                if (!refreshed)
                {
                    // Refresh failed, but we had a token file. Let's return SignedIn as fallback
                    return global::MeetingRecorder.Resources.GoogleDriveSignedIn;
                }
            }

            var driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "MeetingRecorder"
            });

            var aboutRequest = driveService.About.Get();
            aboutRequest.Fields = "user(emailAddress)";
            var about = await aboutRequest.ExecuteAsync(cancellationToken);
            if (about?.User?.EmailAddress != null)
            {
                return string.Format(global::MeetingRecorder.Resources.GoogleDriveSignedInAs, about.User.EmailAddress);
            }

            return global::MeetingRecorder.Resources.GoogleDriveSignedIn;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get detailed account status: {ex.Message}");
            // Double check if token file exists, as a safety fallback
            var tokenFolderPath = MeetingRecorder.App.TokenFolderPath;
            var tokenFilePath = Path.Combine(tokenFolderPath, "dpapi_user.dat");
            if (File.Exists(tokenFilePath))
            {
                return global::MeetingRecorder.Resources.GoogleDriveSignedIn;
            }
            return global::MeetingRecorder.Resources.GoogleDriveNotSignedIn;
        }
    }

    /// <summary>
    /// Force a manual login using either user-supplied OAuth credentials or default injected credentials.
    /// Returns the updated account status string upon successful login.
    /// </summary>
    public async Task<string> LoginAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        await _authSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Clear any active DriveService so we force re-authentication
            _driveService?.Dispose();
            _driveService = null;
            _targetFolderId = null;

            ClientSecrets secrets = ResolveClientSecretsInternal(clientId, clientSecret);

            var tokenFolderPath = MeetingRecorder.App.TokenFolderPath;
            var dpapiStore = new DpapiFileDataStore(tokenFolderPath);

            // This will open a browser window for Google authentication.
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

            // Fetch user info to verify credentials and update status
            var aboutRequest = _driveService.About.Get();
            aboutRequest.Fields = "user(emailAddress)";
            var about = await aboutRequest.ExecuteAsync(cancellationToken);
            if (about?.User?.EmailAddress != null)
            {
                return string.Format(global::MeetingRecorder.Resources.GoogleDriveSignedInAs, about.User.EmailAddress);
            }

            return global::MeetingRecorder.Resources.GoogleDriveSignedIn;
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    public bool IsOrganizing { get; private set; }
    public string OrganizeStatusText { get; private set; } = "";
    public int OrganizeProgressValue { get; private set; }

    public event EventHandler<OrganizeProgressEventArgs>? OrganizeProgressChanged;

    private void ReportProgress(bool isOrganizing, string statusText, int progressValue)
    {
        IsOrganizing = isOrganizing;
        OrganizeStatusText = statusText;
        OrganizeProgressValue = progressValue;
        OrganizeProgressChanged?.Invoke(this, new OrganizeProgressEventArgs(isOrganizing, statusText, progressValue));
    }

    /// <inheritdoc />
    public void StartOrganizeExistingFiles()
    {
        if (IsOrganizing) return;

        // Start background organization task
        _ = Task.Run(async () =>
        {
            try
            {
                ReportProgress(true, "Connecting to Google Drive...", 0);
                var service = await GetDriveServiceAsync(_cts.Token);
                var parentFolderId = await GetOrCreateFolderIdAsync(service, _cts.Token);

                ReportProgress(true, "Listing files...", 10);
                var filesToMove = new List<Google.Apis.Drive.v3.Data.File>();
                string? pageToken = null;
                do
                {
                    var listRequest = service.Files.List();
                    listRequest.Q = $"'{parentFolderId}' in parents and mimeType != 'application/vnd.google-apps.folder' and trashed = false";
                    listRequest.Fields = "nextPageToken, files(id, name, createdTime, parents, createdTimeDateTimeOffset)";
                    listRequest.Spaces = "drive";
                    listRequest.PageToken = pageToken;
                    listRequest.PageSize = 100;

                    var response = await listRequest.ExecuteAsync(_cts.Token);
                    if (response.Files != null)
                    {
                        filesToMove.AddRange(response.Files);
                    }
                    pageToken = response.NextPageToken;
                } while (pageToken != null && !_cts.Token.IsCancellationRequested);

                if (_cts.Token.IsCancellationRequested)
                {
                    ReportProgress(false, "Cancelled", 0);
                    return;
                }

                int total = filesToMove.Count;
                if (total == 0)
                {
                    ReportProgress(false, "No files found directly under the sync folder to organize.", 100);
                    _ = Task.Delay(5000).ContinueWith(_ => { if (!IsOrganizing) ReportProgress(false, "", 0); });
                    return;
                }

                int movedCount = 0;
                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested)
                    {
                        ReportProgress(false, "Cancelled", 0);
                        return;
                    }

                    var file = filesToMove[i];
                    if (string.IsNullOrEmpty(file.Id)) continue;

                    DateTime fileTime = file.CreatedTimeDateTimeOffset?.DateTime ?? DateTime.Now;
                    string monthFolderName = fileTime.ToString("yyyyMM");

                    ReportProgress(true, $"Organizing '{file.Name}' ({movedCount + 1}/{total})...", (int)(10 + (double)i / total * 90));

                    // Get or create the subfolder under parentFolderId
                    var (targetFolderId, _) = await GetOrCreateSingleFolderAsync(service, monthFolderName, parentFolderId, _cts.Token);

                    if (targetFolderId != parentFolderId)
                    {
                        var updateRequest = service.Files.Update(new Google.Apis.Drive.v3.Data.File(), file.Id);
                        updateRequest.AddParents = targetFolderId;
                        if (file.Parents != null && file.Parents.Count > 0)
                        {
                            updateRequest.RemoveParents = string.Join(",", file.Parents);
                        }
                        else
                        {
                            updateRequest.RemoveParents = parentFolderId;
                        }
                        updateRequest.Fields = "id, parents";
                        await updateRequest.ExecuteAsync(_cts.Token);
                        movedCount++;
                    }
                }

                ReportProgress(false, string.Format(global::MeetingRecorder.Resources.OrganizeSuccess, movedCount), 100);
                _ = Task.Delay(5000).ContinueWith(_ => { if (!IsOrganizing) ReportProgress(false, "", 0); });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to organize files in background: {ex.Message}");
                ReportProgress(false, string.Format(global::MeetingRecorder.Resources.OrganizeFailed, ex.Message), 0);
                _ = Task.Delay(5000).ContinueWith(_ => { if (!IsOrganizing) ReportProgress(false, "", 0); });
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _driveService?.Dispose();
        _authSemaphore.Dispose();
        _folderSemaphore.Dispose();
    }
}
