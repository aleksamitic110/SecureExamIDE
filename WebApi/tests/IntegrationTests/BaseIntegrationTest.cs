using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace IntegrationTests;

[Collection(nameof(IntegrationTestCollection))]
public abstract class BaseIntegrationTest
{
    protected BaseIntegrationTest(IntegrationTestWebAppFactory factory)
    {
        HttpClient = factory.CreateClient();
    }

    protected HttpClient HttpClient { get; }

    protected sealed record AccessTokens(
        string AccessToken,
        string RefreshToken,
        Guid DeviceId,
        string DeviceCredential);

    protected sealed record Registration(Guid UserId, Guid DeviceId, string DeviceCredential);

    protected static string UniqueEmail() => $"test-{Guid.NewGuid():N}@example.com";

    // Index numbers are unique per student, so every registration in the suite needs its own.
    // The database is recreated per test run, so a plain counter cannot collide.
    protected static string UniqueIndexNumber() =>
        Interlocked.Increment(ref _nextIndexNumber).ToString(CultureInfo.InvariantCulture);

    private static int _nextIndexNumber = 100000;

    protected async Task<HttpResponseMessage> RegisterAsync(object request) =>
        await HttpClient.PostAsJsonAsync("users/register", request);

    protected async Task<Registration> RegisterStudentAsync(
        string email,
        string? indexNumber = null,
        string deviceName = "Test laptop")
    {
        HttpResponseMessage response = await RegisterAsync(new
        {
            email,
            firstName = "Test",
            lastName = "Student",
            password = "Password123",
            role = "Student",
            indexNumber = indexNumber ?? UniqueIndexNumber(),
            deviceName
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Registration>())!;
    }

    protected async Task<Registration> RegisterProfessorAsync(
        string email,
        string deviceName = "Test office PC")
    {
        HttpResponseMessage response = await RegisterAsync(new
        {
            email,
            firstName = "Test",
            lastName = "Professor",
            password = "Password123",
            role = "Professor",
            professorRegistrationCode = IntegrationTestWebAppFactory.ProfessorRegistrationCode,
            deviceName
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Registration>())!;
    }

    protected async Task<AccessTokens> LoginAsync(string email, string deviceName = "Second machine")
    {
        var request = new { email, password = "Password123", deviceName };

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("users/login", request);
        response.EnsureSuccessStatusCode();

        AccessTokens? tokens = await response.Content.ReadFromJsonAsync<AccessTokens>();

        return tokens!;
    }

    protected async Task<(Guid UserId, AccessTokens Tokens)> RegisterAndLoginAsync()
    {
        string email = UniqueEmail();
        Registration registration = await RegisterStudentAsync(email);
        AccessTokens tokens = await LoginAsync(email);

        return (registration.UserId, tokens);
    }

    protected async Task<(Guid UserId, AccessTokens Tokens)> RegisterAndLoginProfessorAsync()
    {
        string email = UniqueEmail();
        Registration registration = await RegisterProfessorAsync(email);
        AccessTokens tokens = await LoginAsync(email);

        return (registration.UserId, tokens);
    }

    protected async Task<HttpResponseMessage> IssueDeviceTokenAsync(string deviceCredential) =>
        await HttpClient.PostAsJsonAsync("auth/device-token", new { deviceCredential });

    protected void Authenticate(string accessToken)
    {
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
