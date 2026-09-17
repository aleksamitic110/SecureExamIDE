using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Credentials;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Tests.Fakes;

namespace SecureExamIDE.Client.Tests.Session;

public sealed class SessionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly StoredCredential Credential = new("ana@example.com", DeviceId, "device-secret");
    private static readonly UserProfile Student = new(UserId, "ana@example.com", "Ana", "Anic", UserRole.Student, "19252");

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly InMemoryCredentialStore _store = new();
    private readonly InMemoryProfileCache _profiles = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));

    private SessionService CreateService() => new(_api, _store, _profiles, _time);

    private static ApiError ErrorWithCode(string code, int statusCode = 403) => new(statusCode, code, "Refused.", []);

    // The token the API would issue now: valid for 15 minutes.
    private void ServerIssuesTokens()
    {
        _api.IssueDeviceTokenAsync(Arg.Any<DeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => ApiResult.Success(new DeviceTokenResponse(TestTokens.Create(UserId, _time.GetUtcNow().AddMinutes(15)))));

        _api.GetUserAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(Student));
    }

    [Fact]
    public async Task Restore_Should_ReportSignedOut_WhenNothingIsStored()
    {
        // Act
        StartupResult result = await CreateService().RestoreAsync();

        // Assert
        result.Outcome.ShouldBe(StartupOutcome.SignedOut);
        await _api.DidNotReceiveWithAnyArgs().IssueDeviceTokenAsync(default!, default);
    }

    // The point of the device credential: a bound computer gets in without a password.
    [Fact]
    public async Task Restore_Should_SignIn_WithTheStoredCredential()
    {
        // Arrange
        _store.Stored = Credential;
        ServerIssuesTokens();
        SessionService service = CreateService();

        // Act
        StartupResult result = await service.RestoreAsync();

        // Assert
        result.Outcome.ShouldBe(StartupOutcome.SignedIn);
        service.CurrentUser.ShouldBe(Student);
        await _api.Received(1).IssueDeviceTokenAsync(
            Arg.Is<DeviceTokenRequest>(r => r.DeviceCredential == "device-secret"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_Should_SendAnUnverifiedAccountToTheCodeScreen_AndKeepTheCredential()
    {
        // Arrange
        _store.Stored = Credential;
        _api.IssueDeviceTokenAsync(Arg.Any<DeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<DeviceTokenResponse>(ErrorWithCode(ErrorCodes.EmailNotVerified)));

        // Act
        StartupResult result = await CreateService().RestoreAsync();

        // Assert
        result.Outcome.ShouldBe(StartupOutcome.EmailNotVerified);
        result.Email.ShouldBe("ana@example.com");
        _store.Stored.ShouldBe(Credential);
    }

    // Revoked on the server: the stored copy is worthless, so it is removed.
    [Fact]
    public async Task Restore_Should_ForgetACredentialTheServerRevoked()
    {
        // Arrange
        _store.Stored = Credential;
        _api.IssueDeviceTokenAsync(Arg.Any<DeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<DeviceTokenResponse>(ErrorWithCode(ErrorCodes.InvalidDeviceCredential, 401)));

        // Act
        StartupResult result = await CreateService().RestoreAsync();

        // Assert
        result.Outcome.ShouldBe(StartupOutcome.SignedOut);
        _store.Stored.ShouldBeNull();
    }

    // No connection says nothing about the credential, which must survive for the next attempt.
    [Fact]
    public async Task Restore_Should_KeepTheCredential_WhenTheServerIsUnreachable()
    {
        // Arrange
        _store.Stored = Credential;
        _api.IssueDeviceTokenAsync(Arg.Any<DeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<DeviceTokenResponse>(ApiError.Unreachable("No connection.")));

        // Act
        StartupResult result = await CreateService().RestoreAsync();

        // Assert
        result.Outcome.ShouldBe(StartupOutcome.Failed);
        result.Error!.IsUnreachable.ShouldBeTrue();
        _store.Stored.ShouldBe(Credential);
    }

    // The API shows the secret exactly once; it has to be stored before anything else can go wrong.
    [Fact]
    public async Task Register_Should_StoreTheCredentialForThisComputer()
    {
        // Arrange
        _api.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new RegisterResponse(UserId, DeviceId, "device-secret")));

        var request = new RegisterRequest("ana@example.com", "Ana", "Anic", "Password123", UserRole.Student, "19252", null, "laptop");

        // Act
        ApiResult result = await CreateService().RegisterAsync(request);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _store.Stored.ShouldBe(Credential);
    }

    [Fact]
    public async Task Register_Should_StoreNothing_WhenTheServerRefuses()
    {
        // Arrange
        _api.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<RegisterResponse>(ErrorWithCode("Users.EmailNotUnique", 409)));

        var request = new RegisterRequest("ana@example.com", "Ana", "Anic", "Password123", UserRole.Student, "19252", null, "laptop");

        // Act
        ApiResult result = await CreateService().RegisterAsync(request);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        _store.Stored.ShouldBeNull();
    }

    [Fact]
    public async Task Login_Should_StoreTheNewCredential_AndStartTheSession()
    {
        // Arrange
        _api.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new LoginResponse("login-token", "refresh", DeviceId, "device-secret")));
        ServerIssuesTokens();
        SessionService service = CreateService();

        // Act
        ApiResult result = await service.LoginAsync("ana@example.com", "Password123", "second-laptop");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _store.Stored.ShouldBe(Credential);
        service.CurrentUser.ShouldBe(Student);
    }

    // A device token lasts 15 minutes; it is reused until a minute before it runs out.
    [Fact]
    public async Task AccessToken_Should_BeReused_AndRenewedShortlyBeforeItExpires()
    {
        // Arrange
        _store.Stored = Credential;
        ServerIssuesTokens();
        SessionService service = CreateService();
        await service.RestoreAsync();

        // Act
        _time.Advance(TimeSpan.FromMinutes(13));
        ApiResult<string> stillValid = await service.GetAccessTokenAsync();

        _time.Advance(TimeSpan.FromMinutes(1.5));
        ApiResult<string> renewed = await service.GetAccessTokenAsync();

        // Assert
        stillValid.IsSuccess.ShouldBeTrue();
        renewed.IsSuccess.ShouldBeTrue();
        renewed.Value.ShouldNotBe(stillValid.Value);
        await _api.Received(2).IssueDeviceTokenAsync(Arg.Any<DeviceTokenRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccessToken_Should_ExplainWhy_WhenItCannotBeRenewed()
    {
        // Arrange
        _store.Stored = Credential;
        _api.IssueDeviceTokenAsync(Arg.Any<DeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<DeviceTokenResponse>(ApiError.Unreachable("No connection.")));

        // Act
        ApiResult<string> token = await CreateService().GetAccessTokenAsync();

        // Assert
        token.IsSuccess.ShouldBeFalse();
        token.Error.IsUnreachable.ShouldBeTrue();
    }

    [Fact]
    public async Task SignOut_Should_RevokeThisComputer_AndForgetIt()
    {
        // Arrange
        _store.Stored = Credential;
        ServerIssuesTokens();
        SessionService service = CreateService();
        await service.RestoreAsync();

        // Act
        await service.SignOutAsync();

        // Assert
        await _api.Received(1).RevokeDeviceAsync(DeviceId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store.Stored.ShouldBeNull();
        service.CurrentUser.ShouldBeNull();
    }

    // Every successful sign-in leaves the profile behind for a start-up with no connection.
    [Fact]
    public async Task Restore_Should_KeepTheProfileForOfflineUse()
    {
        // Arrange
        _store.Stored = Credential;
        ServerIssuesTokens();

        // Act
        await CreateService().RestoreAsync();

        // Assert
        _profiles.Stored.ShouldBe(Student);
    }

    // The exam room: no connection, but this computer is signed in and knows whose it is.
    [Fact]
    public async Task Restore_Should_OfferOfflineWork_WhenUnreachableAndTheProfileIsKnown()
    {
        // Arrange
        _store.Stored = Credential;
        _profiles.Stored = Student;
        _api.IssueDeviceTokenAsync(Arg.Any<DeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<DeviceTokenResponse>(ApiError.Unreachable("No connection.")));
        SessionService service = CreateService();

        // Act
        StartupResult result = await service.RestoreAsync();
        bool continued = await service.ContinueOfflineAsync();

        // Assert
        result.CanWorkOffline.ShouldBeTrue();
        continued.ShouldBeTrue();
        service.IsOffline.ShouldBeTrue();
        service.CurrentUser.ShouldBe(Student);
    }

    // Unreachable is the only failure that can mean "offline"; a server that answered has spoken.
    [Fact]
    public async Task Restore_Should_NotOfferOfflineWork_WhenTheServerAnsweredWithAnError()
    {
        // Arrange
        _store.Stored = Credential;
        _profiles.Stored = Student;
        _api.IssueDeviceTokenAsync(Arg.Any<DeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<DeviceTokenResponse>(ErrorWithCode("Http.500", 500)));

        // Act
        StartupResult result = await CreateService().RestoreAsync();

        // Assert
        result.CanWorkOffline.ShouldBeFalse();
    }

    [Fact]
    public async Task ContinueOffline_Should_Refuse_WhenTheProfileBelongsToAnotherAccount()
    {
        // Arrange
        _store.Stored = Credential;
        _profiles.Stored = Student with { Email = "someone.else@example.com" };
        SessionService service = CreateService();

        // Act
        bool continued = await service.ContinueOfflineAsync();

        // Assert
        continued.ShouldBeFalse();
        service.CurrentUser.ShouldBeNull();
    }

    [Fact]
    public async Task SignOut_Should_ForgetTheProfileToo()
    {
        // Arrange
        _store.Stored = Credential;
        ServerIssuesTokens();
        SessionService service = CreateService();
        await service.RestoreAsync();

        // Act
        await service.SignOutAsync();

        // Assert
        _profiles.Stored.ShouldBeNull();
    }
}
