using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Session;

// Owns who is signed in on this computer: the stored device credential, the short-lived device
// token exchanged for it, and the profile of the account. Screens call this rather than the API
// client, so the rules for binding a machine live in one place.
public interface ISessionService
{
    UserProfile? CurrentUser { get; }

    // True while working from the cached profile without a connection. Nothing that needs the
    // server - the catalog, downloads, signing out - is offered then.
    bool IsOffline { get; }

    // Run at start-up: turns a stored credential, if there is one, into a signed-in session.
    Task<StartupResult> RestoreAsync(CancellationToken cancellationToken = default);

    // Creates the account and stores the credential for this computer. The address still has to be
    // verified before the session can start.
    Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    // Binds this computer to an existing account and starts the session.
    Task<ApiResult> LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default);

    Task<ApiResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default);

    Task<ApiResult> ResendVerificationCodeAsync(string email, CancellationToken cancellationToken = default);

    // Carries on without the server, as the account last seen on this computer. Returns false when
    // there is nothing to carry on as.
    Task<bool> ContinueOfflineAsync(CancellationToken cancellationToken = default);

    // Exchanges the stored credential for a device token and loads the profile.
    Task<ApiResult> CompleteSignInAsync(CancellationToken cancellationToken = default);

    // A valid device token for API calls, renewed from the stored credential shortly before it
    // expires. A failure says why: this computer is signed out, or the server cannot be reached.
    Task<ApiResult<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    // Forgets this computer: revokes its credential on the server when possible and deletes it here.
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
