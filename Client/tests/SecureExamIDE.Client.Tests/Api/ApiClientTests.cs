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
            $"{{\"id\":\"{userId}\",\"email\":\"prof@example.com\",\"firstName\":\"Jovana\",\"lastName\":\"Nikolic\",\"role\":\"Professor\",\"indexNumber\":null}}");

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
              "publishedAt":"2026-09-10T08:30:00Z","professorFirstName":"Jovana","professorLastName":"Nikolic",
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

    // The API is case-sensitive about the filter, and an unknown value is a 400 rather than the
    // unfiltered list, so the enum name has to reach it exactly.
    [Fact]
    public async Task MyExams_Should_SendTheStatusFilterAsText_AndReadTheStatusBack()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""
            {"items":[{"id":"{{examId}}","title":"Algorithms","subject":"Algorithms and Data Structures",
              "status":"Draft","createdAt":"2026-09-20T08:00:00Z","publishedAt":null,
              "fileCount":2,"dependencyCount":1,"sessionCount":0}],
             "page":1,"pageSize":20,"totalCount":1,"hasNextPage":false,"hasPreviousPage":false}
            """);

        // Act
        ApiResult<PagedList<MyExam>> result = await _client.GetMyExamsAsync(1, 20, ExamStatus.Draft, "token-value");

        // Assert
        MyExam exam = result.Value.Items.ShouldHaveSingleItem();
        exam.Status.ShouldBe(ExamStatus.Draft);
        exam.PublishedAt.ShouldBeNull();
        exam.FileCount.ShouldBe(2);

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/exams/mine");
        request.Query.ShouldBe("?page=1&pageSize=20&status=Draft");
    }

    [Fact]
    public async Task MyExams_Should_AskForEverything_WhenNoStatusIsChosen()
    {
        // Arrange
        _handler.Respond(
            HttpStatusCode.OK,
            """{"items":[],"page":1,"pageSize":20,"totalCount":0,"hasNextPage":false,"hasPreviousPage":false}""");

        // Act
        await _client.GetMyExamsAsync(1, 20, null, "token-value");

        // Assert
        _handler.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=1&pageSize=20");
    }

    // The list leaves the description out, so the editor reads the whole exam before filling a form.
    [Fact]
    public async Task GetExam_Should_ReadTheDescriptionAndContents()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""
            {"id":"{{examId}}","title":"Algorithms","description":"Graphs and sorting","subject":"ADS",
             "status":"Published","createdAt":"2026-09-20T08:00:00Z","publishedAt":"2026-09-21T09:30:00Z",
             "files":[{"id":"{{Guid.NewGuid()}}","fileName":"tasks.pdf","contentType":"application/pdf",
               "sizeBytes":4096,"sha256":"{{new string('b', 64)}}"}],
             "dependencies":[{"id":"{{Guid.NewGuid()}}","name":"GCC","version":"14.2.0","platform":"LinuxX64",
               "contentType":"application/gzip","sizeBytes":2098368}]}
            """);

        // Act
        ApiResult<ExamDetails> result = await _client.GetExamAsync(examId, "token-value");

        // Assert
        result.Value.Description.ShouldBe("Graphs and sorting");
        result.Value.Status.ShouldBe(ExamStatus.Published);
        result.Value.Files.ShouldHaveSingleItem().FileName.ShouldBe("tasks.pdf");
        result.Value.Dependencies.ShouldHaveSingleItem().Platform.ShouldBe(DependencyPlatform.LinuxX64);
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe($"/exams/{examId}");
    }

    [Fact]
    public async Task CreateExam_Should_PostTheThreeFields_AndReadTheNewId()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, $"\"{examId}\"");

        // Act
        ApiResult<Guid> result = await _client.CreateExamAsync("Algorithms", "Graphs", "ADS", "token-value");

        // Assert
        result.Value.ShouldBe(examId);

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Path.ShouldBe("/exams");
        request.Body!.ShouldContain("\"title\":\"Algorithms\"");
        request.Body!.ShouldContain("\"subject\":\"ADS\"");
        request.Authorization.ShouldBe("Bearer token-value");
    }

    [Fact]
    public async Task UpdateExam_Should_PatchTheExam()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.NoContent);

        // Act
        ApiResult result = await _client.UpdateExamAsync(examId, "New title", "Graphs", "ADS", "token-value");

        // Assert
        result.IsSuccess.ShouldBeTrue();

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Patch);
        request.Path.ShouldBe($"/exams/{examId}");
        request.Body!.ShouldContain("\"title\":\"New title\"");
    }

    // A published exam refuses every change; the screen shows what the server says.
    [Fact]
    public async Task UpdateExam_Should_ReportTheServersRefusal_WhenTheExamIsNoLongerADraft()
    {
        // Arrange
        _handler.Respond(
            HttpStatusCode.Conflict,
            "{\"title\":\"Exams.NotDraft\",\"detail\":\"An exam can only be changed while it is a draft, but this exam is 'Published'\",\"status\":409}",
            "application/problem+json");

        // Act
        ApiResult result = await _client.UpdateExamAsync(Guid.NewGuid(), "t", "d", "s", "token-value");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("Exams.NotDraft");
        result.Error.StatusCode.ShouldBe(409);
    }

    [Fact]
    public async Task DeleteExam_Should_DeleteTheExam()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.NoContent);

        // Act
        ApiResult result = await _client.DeleteExamAsync(examId, "token-value");

        // Assert
        result.IsSuccess.ShouldBeTrue();

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Delete);
        request.Path.ShouldBe($"/exams/{examId}");
    }

    // The exam's task files go through the API so the digest it records is its own measurement.
    [Fact]
    public async Task UploadExamFileContent_Should_SendTheFileAsAFormPart_AndReadTheServersDigest()
    {
        // Arrange
        var examId = Guid.NewGuid();
        string digest = new('c', 64);
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""{"objectKey":"exams/{{examId}}/files/abc","sizeBytes":12,"sha256":"{{digest}}"}""");
        using var content = new MemoryStream("task text\n"u8.ToArray());

        // Act
        ApiResult<UploadedExamFile> result = await _client.UploadExamFileContentAsync(
            examId, "tasks.txt", content, "text/plain", "token-value");

        // Assert
        result.Value.Sha256.ShouldBe(digest);
        result.Value.ObjectKey.ShouldStartWith($"exams/{examId}/files/");

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Path.ShouldBe($"/exams/{examId}/files/content");
        request.Body!.ShouldContain("name=file");
        request.Body!.ShouldContain("tasks.txt");
    }

    [Fact]
    public async Task AddExamFile_Should_CommitTheObjectKeyOnly()
    {
        // Arrange
        var examId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, $"\"{fileId}\"");

        // Act
        ApiResult<Guid> result = await _client.AddExamFileAsync(
            examId, $"exams/{examId}/files/abc", "tasks.txt", "token-value");

        // Assert
        result.Value.ShouldBe(fileId);

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe($"/exams/{examId}/files");
        request.Body!.ShouldContain("\"fileName\":\"tasks.txt\"");
        // The digest is never sent: the server measured it in phase one.
        request.Body!.ShouldNotContain("sha256");
    }

    [Fact]
    public async Task CreateDependencyUploadUrl_Should_ReadThePresignedLink()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""
            {"objectKey":"exams/{{examId}}/dependencies/abc",
             "uploadUrl":"http://localhost:9000/exam-packages/d.zip?X-Amz-Signature=abc",
             "expiresAt":"2026-09-26T14:00:00Z"}
            """);

        // Act
        ApiResult<DependencyUploadTarget> result = await _client.CreateDependencyUploadUrlAsync(examId, "token-value");

        // Assert
        result.Value.UploadUrl.Host.ShouldBe("localhost");
        result.Value.UploadUrl.Query.ShouldContain("X-Amz-Signature");
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe($"/exams/{examId}/dependencies/upload-url");
    }

    // The platform has to reach the API as text, and it is never defaulted: a Windows-only compiler
    // offered to a Linux student would only be discovered in the exam room.
    [Fact]
    public async Task AddExamDependency_Should_SendThePlatformAsText()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, $"\"{Guid.NewGuid()}\"");

        // Act
        await _client.AddExamDependencyAsync(
            examId, $"exams/{examId}/dependencies/abc", "GCC", "14.2.0", DependencyPlatform.WindowsX64, "token-value");

        // Assert
        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe($"/exams/{examId}/dependencies");
        request.Body!.ShouldContain("\"platform\":\"WindowsX64\"");
        request.Body!.ShouldContain("\"version\":\"14.2.0\"");
    }

    [Fact]
    public async Task RemoveFileAndDependency_Should_DeleteThroughTheExam()
    {
        // Arrange
        var examId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var dependencyId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.NoContent);
        _handler.Respond(HttpStatusCode.NoContent);

        // Act
        await _client.RemoveExamFileAsync(examId, fileId, "token-value");
        await _client.RemoveExamDependencyAsync(examId, dependencyId, "token-value");

        // Assert
        _handler.Requests[0].Method.ShouldBe(HttpMethod.Delete);
        _handler.Requests[0].Path.ShouldBe($"/exams/{examId}/files/{fileId}");
        _handler.Requests[1].Path.ShouldBe($"/exams/{examId}/dependencies/{dependencyId}");
    }

    [Fact]
    public async Task Publish_Should_PatchThePublishRoute()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.NoContent);

        // Act
        ApiResult result = await _client.PublishExamAsync(examId, "token-value");

        // Assert
        result.IsSuccess.ShouldBeTrue();

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Patch);
        request.Path.ShouldBe($"/exams/{examId}/publish");
    }

    // Publishing an exam with nothing in it is refused, and the screen shows what the server said.
    [Fact]
    public async Task Publish_Should_ReportTheRefusal_WhenTheExamHasNoFiles()
    {
        // Arrange
        _handler.Respond(
            HttpStatusCode.Conflict,
            "{\"title\":\"Exams.NoFiles\",\"detail\":\"An exam must contain at least one file before it can be published\",\"status\":409}",
            "application/problem+json");

        // Act
        ApiResult result = await _client.PublishExamAsync(Guid.NewGuid(), "token-value");

        // Assert
        result.Error!.Code.ShouldBe("Exams.NoFiles");
    }

    // The screens work in local time; this is the one place it becomes UTC, so a sitting cannot be
    // scheduled an hour out.
    [Fact]
    public async Task CreateExamSession_Should_SendTheTimesAsUtc_AndReadTheCodeBack()
    {
        // Arrange
        var examId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""
            {"sessionId":"{{sessionId}}","oneTimeCode":"A2JR3FD3ANTC8DGVAES1",
             "startsAt":"2026-09-27T07:00:00Z","endsAt":"2026-09-27T09:00:00Z",
             "packageSizeBytes":2048,"packageSha256":"{{new string('a', 64)}}"}
            """);
        var starts = new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.FromHours(2));

        // Act
        ApiResult<ScheduledSitting> result = await _client.CreateExamSessionAsync(
            examId, starts, starts.AddHours(2), "token-value");

        // Assert
        result.Value.OneTimeCode.ShouldBe("A2JR3FD3ANTC8DGVAES1");
        result.Value.SessionId.ShouldBe(sessionId);

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Path.ShouldBe($"/exams/{examId}/sessions");
        // 09:00 at +02:00 is 07:00 UTC.
        request.Body!.ShouldContain("2026-09-27T07:00:00Z");
        request.Body!.ShouldContain("2026-09-27T09:00:00Z");
    }

    [Fact]
    public async Task MySittings_Should_FilterByExam_WhenOneIsGiven()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _handler.Respond(
            HttpStatusCode.OK,
            $$"""
            {"items":[{"id":"{{Guid.NewGuid()}}","examId":"{{examId}}","examTitle":"Algorithms",
              "startsAt":"2026-09-27T07:00:00Z","endsAt":"2026-09-27T09:00:00Z",
              "isCancelled":true,"submissionCount":3,"createdAt":"2026-09-26T10:00:00Z"}],
             "page":1,"pageSize":20,"totalCount":1,"hasNextPage":false,"hasPreviousPage":false}
            """);

        // Act
        ApiResult<PagedList<MySitting>> result = await _client.GetMySittingsAsync(1, 20, examId, "token-value");

        // Assert
        MySitting sitting = result.Value.Items.ShouldHaveSingleItem();
        sitting.IsCancelled.ShouldBeTrue();
        sitting.SubmissionCount.ShouldBe(3);

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/sessions/mine");
        request.Query.ShouldBe($"?page=1&pageSize=20&examId={examId}");
    }

    [Fact]
    public async Task MySittings_Should_AskForAllOfThem_WhenNoExamIsGiven()
    {
        // Arrange
        _handler.Respond(
            HttpStatusCode.OK,
            """{"items":[],"page":1,"pageSize":20,"totalCount":0,"hasNextPage":false,"hasPreviousPage":false}""");

        // Act
        await _client.GetMySittingsAsync(1, 20, null, "token-value");

        // Assert
        _handler.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=1&pageSize=20");
    }

    [Fact]
    public async Task CancelSitting_Should_PatchTheCancelRoute()
    {
        // Arrange
        var sittingId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.NoContent);

        // Act
        ApiResult result = await _client.CancelSittingAsync(sittingId, "token-value");

        // Assert
        result.IsSuccess.ShouldBeTrue();

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Patch);
        request.Path.ShouldBe($"/sessions/{sittingId}/cancel");
    }

    // A sitting needs the sealed set of files, so a draft is refused.
    [Fact]
    public async Task CreateExamSession_Should_ReportTheRefusal_WhenTheExamIsADraft()
    {
        // Arrange
        _handler.Respond(
            HttpStatusCode.Conflict,
            "{\"title\":\"ExamSessions.ExamNotPublished\",\"detail\":\"A session can only be scheduled for a published exam\",\"status\":409}",
            "application/problem+json");

        // Act
        ApiResult<ScheduledSitting> result = await _client.CreateExamSessionAsync(
            Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(3), "token-value");

        // Assert
        result.Error!.Code.ShouldBe("ExamSessions.ExamNotPublished");
    }
}
