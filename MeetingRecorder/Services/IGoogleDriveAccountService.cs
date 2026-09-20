using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

/// <summary>
/// Service abstraction for managing Google Drive account authentication, credentials, and connection status.
/// </summary>
public interface IGoogleDriveAccountService
{
    /// <summary>
    /// Checks asynchronously if an authenticated token is stored on disk and retrieves user details.
    /// </summary>
    Task<string> GetAccountStatusStringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces an interactive login using either user-supplied OAuth credentials or default injected credentials.
    /// </summary>
    Task<string> LoginAsync(string clientId, string clientSecret, CancellationToken cancellationToken);

    /// <summary>
    /// Resets cached credentials and forces re-authentication on the next request.
    /// </summary>
    void ResetCredentials();
}
