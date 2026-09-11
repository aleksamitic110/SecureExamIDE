using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Web.Api.Common.Storage;

namespace IntegrationTests;

[Collection(nameof(IntegrationTestCollection))]
public abstract class BaseIntegrationTest
{
    private readonly IntegrationTestWebAppFactory _factory;

    protected BaseIntegrationTest(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        HttpClient = factory.CreateClient();
    }

    protected HttpClient HttpClient { get; }

    // Asks object storage directly whether an object exists. Once the API has forgotten an object
    // it no longer offers any way to ask about it, so this is how a test proves a delete really
    // reached MinIO rather than only the database.
    protected async Task<bool> StorageContainsAsync(string objectKey)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IStorageService storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

        return await storage.StatAsync(objectKey) is not null;
    }

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

    // Every mail the API has sent during the run.
    protected CapturingEmailSender Mailbox => _factory.Mailbox;

    // The code in the most recent mail the API sent to this address, read the way a student would.
    protected string LatestVerificationCodeFor(string email)
    {
        string body = _factory.Mailbox.SentTo(email)[^1].Body;

        return Regex.Match(body, @"\b\d{6}\b", RegexOptions.None, TimeSpan.FromSeconds(1)).Value;
    }

    protected async Task<HttpResponseMessage> VerifyEmailAsync(string email, string? code = null) =>
        await HttpClient.PostAsJsonAsync(
            "users/verify-email",
            new { email, code = code ?? LatestVerificationCodeFor(email) });

    // Registers and, unless told otherwise, verifies the address with the mailed code - the state
    // every test that is not about verification itself needs an account to be in.
    protected async Task<Registration> RegisterStudentAsync(
        string email,
        string? indexNumber = null,
        string deviceName = "Test laptop",
        bool verifyEmail = true)
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

        if (verifyEmail)
        {
            (await VerifyEmailAsync(email)).EnsureSuccessStatusCode();
        }

        return (await response.Content.ReadFromJsonAsync<Registration>())!;
    }

    protected async Task<Registration> RegisterProfessorAsync(
        string email,
        string deviceName = "Test office PC",
        bool verifyEmail = true)
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

        if (verifyEmail)
        {
            (await VerifyEmailAsync(email)).EnsureSuccessStatusCode();
        }

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
