using System.Net;
using System.Net.Http.Json;

namespace IntegrationTests.Devices;

public sealed class DevicesTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record DeviceToken(string AccessToken);

    private sealed record DeviceResponse(Guid Id, string DeviceName, DateTime CreatedAt, DateTime? RevokedAt);

    [Fact]
    public async Task Register_Should_ReturnADeviceCredentialForTheRegisteringMachine()
    {
        // Act
        Registration registration = await RegisterStudentAsync(UniqueEmail(), deviceName: "Aleksa's laptop");

        // Assert
        registration.DeviceId.ShouldNotBe(Guid.Empty);
        registration.DeviceCredential.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeviceToken_Should_BeAcceptedAsBearerToken_WithoutEverLoggingIn()
    {
        // Arrange - registration is the only online step; no login happens in this test.
        Registration registration = await RegisterStudentAsync(UniqueEmail());

        // Act
        HttpResponseMessage response = await IssueDeviceTokenAsync(registration.DeviceCredential);

        // Assert
        response.EnsureSuccessStatusCode();
        DeviceToken? token = await response.Content.ReadFromJsonAsync<DeviceToken>();
        token!.AccessToken.ShouldNotBeNullOrWhiteSpace();

        Authenticate(token.AccessToken);
        HttpResponseMessage profile = await HttpClient.GetAsync($"users/{registration.UserId}");
        profile.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeviceToken_Should_ReturnUnauthorized_WhenCredentialIsUnknown()
    {
        // Act
        HttpResponseMessage response = await IssueDeviceTokenAsync("not-a-real-device-credential");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeviceToken_Should_ReturnBadRequest_WhenCredentialIsEmpty()
    {
        // Act
        HttpResponseMessage response = await IssueDeviceTokenAsync(string.Empty);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_Should_BindASecondMachineWithItsOwnCredential()
    {
        // Arrange
        string email = UniqueEmail();
        Registration registration = await RegisterStudentAsync(email, deviceName: "Home laptop");

        // Act
        AccessTokens tokens = await LoginAsync(email, deviceName: "Faculty desktop");

        // Assert - a distinct credential, so revoking one machine leaves the other working.
        tokens.DeviceId.ShouldNotBe(registration.DeviceId);
        tokens.DeviceCredential.ShouldNotBe(registration.DeviceCredential);

        (await IssueDeviceTokenAsync(registration.DeviceCredential)).EnsureSuccessStatusCode();
        (await IssueDeviceTokenAsync(tokens.DeviceCredential)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetDevices_Should_ListEveryMachineBoundToTheAccount()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email, deviceName: "Home laptop");
        AccessTokens tokens = await LoginAsync(email, deviceName: "Faculty desktop");
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync("devices");

        // Assert
        response.EnsureSuccessStatusCode();
        DeviceResponse[]? devices = await response.Content.ReadFromJsonAsync<DeviceResponse[]>();
        devices!.Length.ShouldBe(2);
        devices.Select(d => d.DeviceName).ShouldContain("Home laptop");
        devices.Select(d => d.DeviceName).ShouldContain("Faculty desktop");
    }

    [Fact]
    public async Task GetDevices_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync("devices");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeDevice_Should_StopTheRevokedCredentialFromMintingTokens()
    {
        // Arrange
        string email = UniqueEmail();
        Registration registration = await RegisterStudentAsync(email, deviceName: "Stolen laptop");
        AccessTokens tokens = await LoginAsync(email, deviceName: "Replacement laptop");
        Authenticate(tokens.AccessToken);

        // The stolen machine still works before revocation.
        (await IssueDeviceTokenAsync(registration.DeviceCredential)).EnsureSuccessStatusCode();

        // Act
        HttpResponseMessage revoke = await HttpClient.DeleteAsync($"devices/{registration.DeviceId}");

        // Assert
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage afterRevocation = await IssueDeviceTokenAsync(registration.DeviceCredential);
        afterRevocation.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The remaining machine is unaffected.
        (await IssueDeviceTokenAsync(tokens.DeviceCredential)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task RevokeDevice_Should_ReturnConflict_WhenRevokedTwice()
    {
        // Arrange
        string email = UniqueEmail();
        Registration registration = await RegisterStudentAsync(email);
        AccessTokens tokens = await LoginAsync(email);
        Authenticate(tokens.AccessToken);

        await HttpClient.DeleteAsync($"devices/{registration.DeviceId}");

        // Act
        HttpResponseMessage response = await HttpClient.DeleteAsync($"devices/{registration.DeviceId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // RevokeDevice is the codebase's first void command (ICommandHandler&lt;TCommand&gt;). This asserts
    // that its validator actually runs, i.e. that the ValidationDecorator is attached to the void
    // command pipeline and not only to the one that returns a response.
    [Fact]
    public async Task RevokeDevice_Should_ReturnBadRequest_WhenDeviceIdIsEmpty()
    {
        // Arrange
        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await HttpClient.DeleteAsync($"devices/{Guid.Empty}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevokeDevice_Should_ReturnNotFound_WhenDeviceBelongsToAnotherAccount()
    {
        // Arrange
        Registration victim = await RegisterStudentAsync(UniqueEmail());

        (Guid _, AccessTokens attackerTokens) = await RegisterAndLoginAsync();
        Authenticate(attackerTokens.AccessToken);

        // Act
        HttpResponseMessage response = await HttpClient.DeleteAsync($"devices/{victim.DeviceId}");

        // Assert - reported as missing, not forbidden, so device ids cannot be probed.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the victim's machine still works.
        HttpClient.DefaultRequestHeaders.Authorization = null;
        (await IssueDeviceTokenAsync(victim.DeviceCredential)).EnsureSuccessStatusCode();
    }
}
