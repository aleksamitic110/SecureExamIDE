using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace IntegrationTests.Submissions;

// The offline half of the story: the student's laptop hands in work using the credential it was
// given at registration, never a password login.
public sealed class SubmissionTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const string Solution = "encrypted-solution-bytes";

    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record SubmissionCreated(Guid SubmissionId, DateTime SubmittedAt, long SizeBytes, string Sha256);

    private sealed record SessionCreated(Guid SessionId, string OneTimeCode);

    private sealed record MySubmission(
        Guid Id,
        Guid SessionId,
        Guid ExamId,
        string ExamTitle,
        DateTime SubmittedAt,
        long SizeBytes,
        string Sha256);

    private sealed record MySubmissionsPage(MySubmission[] Items, int Page, int PageSize, int TotalCount);

    // Publishes an exam and opens a sitting that is already under way.
    private async Task<Guid> OpenSessionAsync()
    {
        (Guid _, AccessTokens professor) = await RegisterAndLoginProfessorAsync();
        Authenticate(professor.AccessToken);

        HttpResponseMessage created = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Compilers",
            description = "Write a parser.",
            subject = $"Subject {Guid.NewGuid()}"
        });

        created.EnsureSuccessStatusCode();
        Guid examId = await created.Content.ReadFromJsonAsync<Guid>();

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Task 1."));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "task.txt");

        HttpResponseMessage upload = await HttpClient.PostAsync($"exams/{examId}/files/content", form);
        upload.EnsureSuccessStatusCode();
        UploadedContent uploaded = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        HttpResponseMessage commit = await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/files",
            new { objectKey = uploaded.ObjectKey, fileName = "task.txt", sha256 = uploaded.Sha256 });
        commit.EnsureSuccessStatusCode();

        HttpResponseMessage publish = await HttpClient.PatchAsync($"exams/{examId}/publish", content: null);
        publish.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage session = await HttpClient.PostAsJsonAsync($"exams/{examId}/sessions", new
        {
            startsAt = DateTime.UtcNow.AddHours(-1),
            endsAt = DateTime.UtcNow.AddHours(2)
        });

        session.EnsureSuccessStatusCode();

        return (await session.Content.ReadFromJsonAsync<SessionCreated>())!.SessionId;
    }

    // Registers a student and authenticates the way the desktop client does: with the credential
    // stored on the machine, exchanged for a short-lived token. No password is sent.
    private async Task<Registration> AuthenticateAsDeviceAsync()
    {
        Registration registration = await RegisterStudentAsync(UniqueEmail());

        HttpClient.DefaultRequestHeaders.Authorization = null;

        HttpResponseMessage tokenResponse = await IssueDeviceTokenAsync(registration.DeviceCredential);
        tokenResponse.EnsureSuccessStatusCode();

        AccessTokens tokens = (await tokenResponse.Content.ReadFromJsonAsync<AccessTokens>())!;
        Authenticate(tokens.AccessToken);

        return registration;
    }

    private async Task<UploadedContent> UploadSolutionAsync(Guid sessionId, string content = Solution)
    {
        HttpResponseMessage response = await PostSolutionAsync(sessionId, content);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadedContent>())!;
    }

    private async Task<HttpResponseMessage> SubmitAsync(Guid sessionId, string objectKey, string sha256) =>
        await HttpClient.PostAsJsonAsync(
            $"sessions/{sessionId}/submissions",
            new { objectKey, sha256 });

    [Fact]
    public async Task AStudent_Should_SubmitWithADeviceTokenAndSeeItListed()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();

        // Act
        UploadedContent uploaded = await UploadSolutionAsync(sessionId);
        HttpResponseMessage submitted = await SubmitAsync(sessionId, uploaded.ObjectKey, uploaded.Sha256);

        // Assert
        submitted.EnsureSuccessStatusCode();
        SubmissionCreated created = (await submitted.Content.ReadFromJsonAsync<SubmissionCreated>())!;

        // The digest is the server's own measurement of the bytes it received.
        created.Sha256.ShouldBe(Sha256Of(Solution));
        created.SizeBytes.ShouldBe(Solution.Length);

        HttpResponseMessage mine = await HttpClient.GetAsync("submissions/mine");
        mine.EnsureSuccessStatusCode();

        MySubmissionsPage page = (await mine.Content.ReadFromJsonAsync<MySubmissionsPage>())!;
        page.TotalCount.ShouldBe(1);
        page.Items[0].Id.ShouldBe(created.SubmissionId);
        page.Items[0].SessionId.ShouldBe(sessionId);
        page.Items[0].ExamTitle.ShouldBe("Compilers");
    }

    [Fact]
    public async Task Upload_Should_CreateNoSubmissionRow()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();

        // Act
        await UploadSolutionAsync(sessionId);

        // Assert - the solution is in storage, but nothing is handed in until the commit call.
        HttpResponseMessage mine = await HttpClient.GetAsync("submissions/mine");
        MySubmissionsPage page = (await mine.Content.ReadFromJsonAsync<MySubmissionsPage>())!;
        page.TotalCount.ShouldBe(0);
    }

    // The property the exam mode exists to provide: handed-in work cannot be replaced.
    [Fact]
    public async Task ASecondSubmission_Should_BeRefused()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();

        UploadedContent first = await UploadSolutionAsync(sessionId);
        HttpResponseMessage submitted = await SubmitAsync(sessionId, first.ObjectKey, first.Sha256);
        submitted.EnsureSuccessStatusCode();

        // Act - upload different work and try to hand that in instead.
        HttpResponseMessage secondUpload = await PostSolutionAsync(sessionId, "a better answer");

        // Assert - refused at the earlier phase already, so nothing new even reaches storage.
        secondUpload.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // Submitting is the one action that must come from the machine that sat the exam.
    [Fact]
    public async Task APasswordLogin_Should_NotBeAbleToSubmit()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();

        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await PostSolutionAsync(sessionId, Solution);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // The student id is part of the object key prefix precisely so this cannot work.
    [Fact]
    public async Task AStudent_Should_NotBeAbleToClaimAnotherStudentsUpload()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();

        await AuthenticateAsDeviceAsync();
        UploadedContent victimsUpload = await UploadSolutionAsync(sessionId, "the other student's work");

        // A second student, on their own machine.
        await AuthenticateAsDeviceAsync();

        // Act
        HttpResponseMessage response = await SubmitAsync(
            sessionId, victimsUpload.ObjectKey, victimsUpload.Sha256);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_Should_BeRejected_WhenNothingWasUploadedUnderThatKey()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        Registration student = await AuthenticateAsDeviceAsync();

        string inventedKey = $"sessions/{sessionId}/submissions/{student.UserId}/{Guid.NewGuid()}";

        // Act
        HttpResponseMessage response = await SubmitAsync(sessionId, inventedKey, Sha256Of(Solution));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Submissions_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        HttpClient.DefaultRequestHeaders.Authorization = null;

        // Act
        HttpResponseMessage response = await PostSolutionAsync(sessionId, Solution);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MySubmissions_Should_ShowOnlyTheCallersOwnWork()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();

        await AuthenticateAsDeviceAsync();
        UploadedContent uploaded = await UploadSolutionAsync(sessionId);
        (await SubmitAsync(sessionId, uploaded.ObjectKey, uploaded.Sha256)).EnsureSuccessStatusCode();

        // A different student who has submitted nothing.
        await AuthenticateAsDeviceAsync();

        // Act
        HttpResponseMessage mine = await HttpClient.GetAsync("submissions/mine");

        // Assert
        MySubmissionsPage page = (await mine.Content.ReadFromJsonAsync<MySubmissionsPage>())!;
        page.TotalCount.ShouldBe(0);
    }

    // Builds and disposes the multipart body in one place, so no caller has to own it.
    private async Task<HttpResponseMessage> PostSolutionAsync(Guid sessionId, string content)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", "solution.bin");

        return await HttpClient.PostAsync($"sessions/{sessionId}/submissions/content", form);
    }

    private static string Sha256Of(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
