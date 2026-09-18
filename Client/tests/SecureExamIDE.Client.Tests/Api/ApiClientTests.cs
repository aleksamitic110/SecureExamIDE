using System.Net;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Tests.Fakes;

namespace SecureExamIDE.Client.Tests.Api;

public sealed class ApiClientTests : IDisposable
{
    private readonly StubHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly ApiClient _client;

    public ApiClientTests()
    {
        _httpClient = new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("http://api.test/") };
        _client = new ApiClient(_httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task Register_Should_SendTheRoleAsText_AndReadTheCredential()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, $"{{\"userId\":\"{userId}\",\"deviceId\":\"{deviceId}\",\"deviceCredential\":\"secret\"}}");

        // Act
        ApiResult<RegisterResponse> result = await _client.RegisterAsync(new RegisterRequest(
            "ana@example.com", "Ana", "Anic", "Password123", UserRole.Student, "19252", null, "laptop"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(userId);
        result.Value.DeviceCredential.ShouldBe("secret");

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Path.ShouldBe("/users/register");
        request.Body!.ShouldContain("\"role\":\"Student\"");
    }

    // The client branches on the server's error code, so it has to come through exactly.
    [Fact]
    public async Task Calls_Should_ReadTheErrorCodeAndMessage_FromProblemDetails()
    {
        // Arrange
        _handler.Respond(
            HttpStatusCode.Forbidden,
            "{\"title\":\"Users.EmailNotVerified\",\"detail\":\"The e-mail address has not been verified yet.\",\"status\":403}",
            "application/problem+json");

        // Act
        ApiResult<DeviceTokenResponse> result = await _client.IssueDeviceTokenAsync(new DeviceTokenRequest("secret"));

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.StatusCode.ShouldBe(403);
        result.Error.Code.ShouldBe(ErrorCodes.EmailNotVerified);
        result.Error.Message.ShouldBe("The e-mail address has not been verified yet.");
    }

    [Fact]
    public async Task Calls_Should_ShowEveryValidationMessage()
    {
        // Arrange
        _handler.Respond(
            HttpStatusCode.BadRequest,
            "{\"title\":\"Validation.General\",\"detail\":\"One or more validation errors occurred\"," +
            "\"errors\":[{\"code\":\"a\",\"description\":\"Email is required.\"},{\"code\":\"b\",\"description\":\"Index number must consist of 4 to 6 digits.\"}]}",
            "application/problem+json");

        // Act
        ApiResult result = await _client.VerifyEmailAsync(new VerifyEmailRequest("", "123456"));

        // Assert
        result.Error!.ValidationMessages.Count.ShouldBe(2);
        result.Error.Message.ShouldContain("Email is required.");
        result.Error.Message.ShouldContain("Index number must consist of 4 to 6 digits.");
    }

    // The rate limiter answers with an empty body; the user still needs a sentence, not a crash.
    [Fact]
    public async Task Calls_Should_ExplainAnEmptyTooManyRequestsResponse()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.TooManyRequests);

        // Act
        ApiResult result = await _client.ResendVerificationCodeAsync(new ResendVerificationCodeRequest("ana@example.com"));

        // Assert
        result.Error!.StatusCode.ShouldBe(429);
        result.Error.Message.ShouldBe("Too many attempts. Wait a minute and try again.");
    }

    [Fact]
    public async Task Calls_Should_ReportAnUnreachableServer_InsteadOfThrowing()
    {
        // Arrange
        _handler.Fail(new HttpRequestException("Connection refused"));

        // Act
        ApiResult<LoginResponse> result = await _client.LoginAsync(new LoginRequest("ana@example.com", "Password123", "laptop"));

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.IsUnreachable.ShouldBeTrue();
        result.Error.Message.ShouldContain("http://api.test/");
    }

    [Fact]
    public async Task GetUser_Should_SendTheBearerToken_AndReadTheRole()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $"{{\"id\":\"{userId}\",\"email\":\"prof@example.com\",\"firstName\":\"Milena\",\"lastName\":\"Frtunic\",\"role\":\"Professor\",\"indexNumber\":null}}");

        // Act
        ApiResult<UserProfile> result = await _client.GetUserAsync(userId, "token-value");

        // Assert
        result.Value.Role.ShouldBe(UserRole.Professor);

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe($"/users/{userId}");
        request.Authorization.ShouldBe("Bearer token-value");
    }

    [Fact]
    public async Task Calls_Should_ExplainAMissingServerAddress_WithoutSendingAnything()
    {
        // Arrange
        using var unconfigured = new HttpClient(_handler, disposeHandler: false);
        var client = new ApiClient(unconfigured);

        // Act
        ApiResult result = await client.VerifyEmailAsync(new VerifyEmailRequest("ana@example.com", "123456"));

        // Assert
        result.Error!.IsUnreachable.ShouldBeTrue();
        result.Error.Message.ShouldContain("not configured");
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Catalog_Should_AskForThePage_AndReadTimesAsUtc()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""
            {"items":[{"id":"{{examId}}","title":"Algorithms","description":"Graphs","subject":"Algorithms and Data Structures",
              "publishedAt":"2026-09-10T08:30:00Z","professorFirstName":"Milena","professorLastName":"Frtunic",
              "fileCount":2,"dependencyCount":1,"totalSizeBytes":1048576}],
             "page":2,"pageSize":20,"totalCount":21,"hasNextPage":false,"hasPreviousPage":true}
            """);

        // Act
        ApiResult<PagedList<CatalogExam>> result = await _client.GetPublishedExamsAsync(2, 20, "token-value");

        // Assert
        CatalogExam exam = result.Value.Items.ShouldHaveSingleItem();
        exam.Id.ShouldBe(examId);
        exam.PublishedAt.ShouldBe(new DateTimeOffset(2026, 9, 10, 8, 30, 0, TimeSpan.Zero));
        result.Value.HasPreviousPage.ShouldBeTrue();

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/exams");
        request.Query.ShouldBe("?page=2&pageSize=20");
        request.Authorization.ShouldBe("Bearer token-value");
    }

    // A client asks only for what it can run: its own platform's toolchains plus the ones marked Any.
    [Fact]
    public async Task Dependencies_Should_AskForThisComputersPlatform()
    {
        // Arrange
        var dependencyId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""
            {"items":[{"id":"{{dependencyId}}","name":"GCC (MinGW-w64)","version":"14.2.0","platform":"WindowsX64",
              "contentType":"application/zip","sizeBytes":2098368}],
             "page":1,"pageSize":100,"totalCount":1,"hasNextPage":false,"hasPreviousPage":false}
            """);

        // Act
        ApiResult<PagedList<ExamDependency>> result = await _client.GetExamDependenciesAsync(
            examId, 1, 100, DependencyPlatform.WindowsX64, "token-value");

        // Assert
        ExamDependency dependency = result.Value.Items.ShouldHaveSingleItem();
        dependency.Platform.ShouldBe(DependencyPlatform.WindowsX64);

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe($"/exams/{examId}/dependencies");
        request.Query.ShouldBe("?page=1&pageSize=100&platform=WindowsX64");
    }

    [Fact]
    public async Task SittingPackage_Should_ReadTheStorageLinks()
    {
        // Arrange
        var sittingId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""
            {"sessionId":"{{sittingId}}","packageUrl":"http://localhost:9000/exam-packages/p.bin?X-Amz-Signature=abc",
             "headerUrl":"http://localhost:9000/exam-packages/p.hdr?X-Amz-Signature=def","expiresAt":"2026-09-16T17:00:00Z",
             "packageSizeBytes":2048,"packageSha256":"{{new string('a', 64)}}"}
            """);

        // Act
        ApiResult<SittingPackage> result = await _client.GetSittingPackageAsync(sittingId, "token-value");

        // Assert
        result.Value.PackageUrl.Host.ShouldBe("localhost");
        result.Value.HeaderUrl.Query.ShouldContain("X-Amz-Signature=def");
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe($"/sessions/{sittingId}/package");
    }
}
