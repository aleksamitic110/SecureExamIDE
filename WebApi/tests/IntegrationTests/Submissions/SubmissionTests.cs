using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace IntegrationTests.Submissions;

// The offline half of the story: the student's laptop hands in its work - the solution and the
// activity log together - using the credential it was given at registration, never a password login.
public sealed class SubmissionTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const string SolutionText = "encrypted-solution-bytes";
    private const string ActivityLogText = "encrypted-activity-log-bytes";

    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record UploadedSubmission(UploadedContent Solution, UploadedContent ActivityLog);

    private sealed record SubmissionCreated(
        Guid SubmissionId,
        DateTime SubmittedAt,
        long SolutionSizeBytes,
        string SolutionSha256,
        long ActivityLogSizeBytes,
        string ActivityLogSha256);

    private sealed record SessionCreated(Guid SessionId, string OneTimeCode);

    private sealed record MySubmission(
        Guid Id,
        Guid SessionId,
        Guid ExamId,
        string ExamTitle,
        DateTime SubmittedAt,
        long SolutionSizeBytes,
        string SolutionSha256,
        long ActivityLogSizeBytes,
        string ActivityLogSha256);

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
            new { objectKey = uploaded.ObjectKey, fileName = "task.txt" });
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

    private async Task<UploadedSubmission> UploadSubmissionAsync(Guid sessionId, string solution = SolutionText)
    {
        HttpResponseMessage response = await PostContentAsync(sessionId, solution, ActivityLogText);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadedSubmission>())!;
    }

    private async Task<HttpResponseMessage> SubmitAsync(
        Guid sessionId,
        UploadedContent solution,
        UploadedContent activityLog) =>
        await HttpClient.PostAsJsonAsync(
            $"sessions/{sessionId}/submissions",
            new { solutionObjectKey = solution.ObjectKey, activityLogObjectKey = activityLog.ObjectKey });

    private async Task<HttpResponseMessage> SubmitAsync(Guid sessionId, UploadedSubmission uploaded) =>
        await SubmitAsync(sessionId, uploaded.Solution, uploaded.ActivityLog);

    [Fact]
    public async Task AStudent_Should_SubmitWithADeviceTokenAndSeeItListed()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();

        // Act
        UploadedSubmission uploaded = await UploadSubmissionAsync(sessionId);
        HttpResponseMessage submitted = await SubmitAsync(sessionId, uploaded);

        // Assert
        submitted.EnsureSuccessStatusCode();
        SubmissionCreated created = (await submitted.Content.ReadFromJsonAsync<SubmissionCreated>())!;

        // Each digest is the server's own measurement of the bytes it received.
        created.SolutionSha256.ShouldBe(Sha256Of(SolutionText));
        created.SolutionSizeBytes.ShouldBe(SolutionText.Length);
        created.ActivityLogSha256.ShouldBe(Sha256Of(ActivityLogText));
        created.ActivityLogSizeBytes.ShouldBe(ActivityLogText.Length);

        HttpResponseMessage mine = await HttpClient.GetAsync("submissions/mine");
        mine.EnsureSuccessStatusCode();

        MySubmissionsPage page = (await mine.Content.ReadFromJsonAsync<MySubmissionsPage>())!;
        page.TotalCount.ShouldBe(1);
        page.Items[0].Id.ShouldBe(created.SubmissionId);
        page.Items[0].SessionId.ShouldBe(sessionId);
        page.Items[0].ExamTitle.ShouldBe("Compilers");
        page.Items[0].ActivityLogSha256.ShouldBe(Sha256Of(ActivityLogText));
    }

    // The digests are what the server measured at upload, stored with each object. Digests the
    // client adds to the commit are not part of the contract and change nothing.
    [Fact]
    public async Task Submit_Should_RecordTheServersDigests_NotOnesTheClientSends()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();
        UploadedSubmission uploaded = await UploadSubmissionAsync(sessionId);
        string forged = Sha256Of("something else entirely");

        // Act
        HttpResponseMessage submitted = await HttpClient.PostAsJsonAsync(
            $"sessions/{sessionId}/submissions",
            new
            {
                solutionObjectKey = uploaded.Solution.ObjectKey,
                solutionSha256 = forged,
                activityLogObjectKey = uploaded.ActivityLog.ObjectKey,
                activityLogSha256 = forged
            });

        // Assert
        submitted.EnsureSuccessStatusCode();
        SubmissionCreated created = (await submitted.Content.ReadFromJsonAsync<SubmissionCreated>())!;
        created.SolutionSha256.ShouldBe(Sha256Of(SolutionText));
        created.ActivityLogSha256.ShouldBe(Sha256Of(ActivityLogText));
    }

    [Fact]
    public async Task Upload_Should_CreateNoSubmissionRow()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();

        // Act
        await UploadSubmissionAsync(sessionId);

        // Assert - both parts are in storage, but nothing is handed in until the commit call.
        HttpResponseMessage mine = await HttpClient.GetAsync("submissions/mine");
        MySubmissionsPage page = (await mine.Content.ReadFromJsonAsync<MySubmissionsPage>())!;
        page.TotalCount.ShouldBe(0);
    }

    // The log travels with the solution. A request with the solution alone is not a submission.
    [Fact]
    public async Task Upload_Should_BeRejected_WhenTheActivityLogIsMissing()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();

        // Act
        HttpResponseMessage response = await PostContentAsync(sessionId, SolutionText, activityLog: null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // The part is in the object key, so the solution cannot be handed in a second time as the log.
    [Fact]
    public async Task ASolution_Should_NotBeAcceptedInPlaceOfTheActivityLog()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();
        UploadedSubmission uploaded = await UploadSubmissionAsync(sessionId);

        // Act
        HttpResponseMessage response = await SubmitAsync(sessionId, uploaded.Solution, uploaded.Solution);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // The property the exam mode exists to provide: handed-in work cannot be replaced.
    [Fact]
    public async Task ASecondSubmission_Should_BeRefused()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        await AuthenticateAsDeviceAsync();

        UploadedSubmission first = await UploadSubmissionAsync(sessionId);
        HttpResponseMessage submitted = await SubmitAsync(sessionId, first);
        submitted.EnsureSuccessStatusCode();

        // Act - upload different work and try to hand that in instead.
        HttpResponseMessage secondUpload = await PostContentAsync(sessionId, "a better answer", ActivityLogText);

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
        HttpResponseMessage response = await PostContentAsync(sessionId, SolutionText, ActivityLogText);

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
        UploadedSubmission victimsUpload = await UploadSubmissionAsync(sessionId, "the other student's work");

        // A second student, on their own machine.
        await AuthenticateAsDeviceAsync();

        // Act
        HttpResponseMessage response = await SubmitAsync(sessionId, victimsUpload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_Should_BeRejected_WhenNothingWasUploadedUnderThatKey()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();
        Registration student = await AuthenticateAsDeviceAsync();
        UploadedSubmission uploaded = await UploadSubmissionAsync(sessionId);

        // A well-formed key under this student's own solution prefix, which was never written.
        var inventedSolution = new UploadedContent(
            $"sessions/{sessionId}/submissions/{student.UserId}/solution/{Guid.NewGuid()}",
            SolutionText.Length,
            Sha256Of(SolutionText));

        // Act
        HttpResponseMessage response = await SubmitAsync(sessionId, inventedSolution, uploaded.ActivityLog);

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
        HttpResponseMessage response = await PostContentAsync(sessionId, SolutionText, ActivityLogText);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MySubmissions_Should_ShowOnlyTheCallersOwnWork()
    {
        // Arrange
        Guid sessionId = await OpenSessionAsync();

        await AuthenticateAsDeviceAsync();
        UploadedSubmission uploaded = await UploadSubmissionAsync(sessionId);
        (await SubmitAsync(sessionId, uploaded)).EnsureSuccessStatusCode();

        // A different student who has submitted nothing.
        await AuthenticateAsDeviceAsync();

        // Act
        HttpResponseMessage mine = await HttpClient.GetAsync("submissions/mine");

        // Assert
        MySubmissionsPage page = (await mine.Content.ReadFromJsonAsync<MySubmissionsPage>())!;
        page.TotalCount.ShouldBe(0);
    }

    // Builds and disposes the multipart body in one place, so no caller has to own it. A null
    // activity log sends the solution on its own.
    private async Task<HttpResponseMessage> PostContentAsync(Guid sessionId, string solution, string? activityLog)
    {
        using var form = new MultipartFormDataContent();

        using var solutionContent = new ByteArrayContent(Encoding.UTF8.GetBytes(solution));
        solutionContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(solutionContent, "solution", "solution.bin");

        using ByteArrayContent? activityLogContent = activityLog is null
            ? null
            : new ByteArrayContent(Encoding.UTF8.GetBytes(activityLog));

        if (activityLogContent is not null)
        {
            activityLogContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(activityLogContent, "activityLog", "activity-log.bin");
        }

        return await HttpClient.PostAsync($"sessions/{sessionId}/submissions/content", form);
    }

    private static string Sha256Of(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
