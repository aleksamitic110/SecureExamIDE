using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Credentials;

namespace SecureExamIDE.Client.Services.Session;

internal sealed class SessionService(
    IApiClient apiClient,
    ICredentialStore credentialStore,
    IProfileCache profileCache,
    TimeProvider timeProvider) : ISessionService
{
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public UserProfile? CurrentUser { get; private set; }

    public bool IsOffline { get; private set; }

    public async Task<StartupResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        StoredCredential? credential = await credentialStore.LoadAsync(cancellationToken);

        if (credential is null)
        {
            return new StartupResult(StartupOutcome.SignedOut);
        }

        ApiResult signIn = await CompleteSignInAsync(credential, cancellationToken);

        if (signIn.IsSuccess)
        {
            return new StartupResult(StartupOutcome.SignedIn, credential.Email);
        }

        switch (signIn.Error.Code)
        {
            case ErrorCodes.EmailNotVerified:
                return new StartupResult(StartupOutcome.EmailNotVerified, credential.Email, signIn.Error);

            // Revoked on the server, from another machine or by an administrator: the stored copy is
            // worthless, so it is removed and the user starts again from the welcome screen.
            case ErrorCodes.InvalidDeviceCredential:
                await credentialStore.DeleteAsync(cancellationToken);
                return new StartupResult(StartupOutcome.SignedOut, credential.Email, signIn.Error);

            default:
                bool canWorkOffline = signIn.Error.IsUnreachable &&
                                      await profileCache.LoadAsync(cancellationToken) is { } cached &&
                                      string.Equals(cached.Email, credential.Email, StringComparison.OrdinalIgnoreCase);

                return new StartupResult(StartupOutcome.Failed, credential.Email, signIn.Error, canWorkOffline);
        }
    }

    public async Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        ApiResult<RegisterResponse> registered = await apiClient.RegisterAsync(request, cancellationToken);

        if (!registered.IsSuccess)
        {
            return registered;
        }

        // Stored immediately: the API shows this secret once and never again.
        await credentialStore.SaveAsync(
            new StoredCredential(request.Email, registered.Value.DeviceId, registered.Value.DeviceCredential),
            cancellationToken);

        return ApiResult.Success();
    }

    public async Task<ApiResult> LoginAsync(
        string email,
        string password,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        ApiResult<LoginResponse> loggedIn = await apiClient.LoginAsync(
            new LoginRequest(email, password, deviceName), cancellationToken);

        if (!loggedIn.IsSuccess)
        {
            return loggedIn;
        }

        StoredCredential credential = new(email, loggedIn.Value.DeviceId, loggedIn.Value.DeviceCredential);
        await credentialStore.SaveAsync(credential, cancellationToken);

        // The login's own token is not kept: every session runs on a device token, the same way it
        // will on exam day, so there is one path to test rather than two.
        return await CompleteSignInAsync(credential, cancellationToken);
    }

    public Task<ApiResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default) =>
        apiClient.VerifyEmailAsync(new VerifyEmailRequest(email, code), cancellationToken);

    public Task<ApiResult> ResendVerificationCodeAsync(string email, CancellationToken cancellationToken = default) =>
        apiClient.ResendVerificationCodeAsync(new ResendVerificationCodeRequest(email), cancellationToken);

    public async Task<bool> ContinueOfflineAsync(CancellationToken cancellationToken = default)
    {
        StoredCredential? credential = await credentialStore.LoadAsync(cancellationToken);
        UserProfile? cached = await profileCache.LoadAsync(cancellationToken);

        if (credential is null || cached is null ||
            !string.Equals(cached.Email, credential.Email, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CurrentUser = cached;
        IsOffline = true;

        return true;
    }

    public async Task<ApiResult> CompleteSignInAsync(CancellationToken cancellationToken = default)
    {
        StoredCredential? credential = await credentialStore.LoadAsync(cancellationToken);

        return credential is null
            ? ApiResult.Failure(new ApiError(401, ErrorCodes.InvalidDeviceCredential, "This computer is not signed in.", []))
            : await CompleteSignInAsync(credential, cancellationToken);
    }

    public async Task<ApiResult<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_accessToken is not null && _accessTokenExpiresAt - timeProvider.GetUtcNow() > RenewalMargin)
        {
            return ApiResult.Success(_accessToken);
        }

        StoredCredential? credential = await credentialStore.LoadAsync(cancellationToken);

        if (credential is null)
        {
            return ApiResult.Failure<string>(SignedOut);
        }

        // The reason a renewal failed is passed on: no connection and a revoked computer call for
        // different messages.
        ApiResult renewed = await IssueDeviceTokenAsync(credential, cancellationToken);

        return renewed.IsSuccess ? ApiResult.Success(_accessToken!) : ApiResult.Failure<string>(renewed.Error);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        StoredCredential? credential = await credentialStore.LoadAsync(cancellationToken);

        // Best effort: revoking on the server makes the credential useless even if a copy of it
        // survived somewhere. If the server is unreachable, the local copy is still removed.
        if (credential is not null && await GetAccessTokenAsync(cancellationToken) is { IsSuccess: true } token)
        {
            await apiClient.RevokeDeviceAsync(credential.DeviceId, token.Value, cancellationToken);
        }

        await credentialStore.DeleteAsync(cancellationToken);
        await profileCache.DeleteAsync(cancellationToken);

        _accessToken = null;
        CurrentUser = null;
    }

    private async Task<ApiResult> CompleteSignInAsync(StoredCredential credential, CancellationToken cancellationToken)
    {
        ApiResult issued = await IssueDeviceTokenAsync(credential, cancellationToken);

        if (!issued.IsSuccess)
        {
            return issued;
        }

        Guid? userId = AccessTokenClaims.ReadUserId(_accessToken!);

        if (userId is null)
        {
            return ApiResult.Failure(new ApiError(0, "Client.UnexpectedResponse", "The server issued a token the application could not read.", []));
        }

        ApiResult<UserProfile> profile = await apiClient.GetUserAsync(userId.Value, _accessToken!, cancellationToken);

        if (!profile.IsSuccess)
        {
            return profile;
        }

        CurrentUser = profile.Value;
        IsOffline = false;

        // Kept for exam day, when the same computer may start with no connection at all.
        await profileCache.SaveAsync(profile.Value, cancellationToken);

        return ApiResult.Success();
    }

    private async Task<ApiResult> IssueDeviceTokenAsync(StoredCredential credential, CancellationToken cancellationToken)
    {
        ApiResult<DeviceTokenResponse> issued = await apiClient.IssueDeviceTokenAsync(
            new DeviceTokenRequest(credential.DeviceCredential), cancellationToken);

        if (!issued.IsSuccess)
        {
            _accessToken = null;
            return issued;
        }

        _accessToken = issued.Value.AccessToken;

        // A token without a readable expiry is used once and renewed on the next call.
        _accessTokenExpiresAt = AccessTokenClaims.ReadExpiry(_accessToken) ?? timeProvider.GetUtcNow();

        return ApiResult.Success();
    }

    private static readonly ApiError SignedOut =
        new(401, "Client.SignedOut", "This computer is no longer signed in. Sign in again.", []);

    // Renewed a minute early, so a call started just before expiry does not arrive with a dead token.
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(1);
}
