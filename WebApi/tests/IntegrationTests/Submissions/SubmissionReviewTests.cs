using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace IntegrationTests.Submissions;

// The professor's half of the hand-in: the work for a sitting of their own exam can be listed and
// fetched, the files come back byte for byte, and no other account sees any of it.
public sealed class SubmissionReviewTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const string SolutionText = "encrypted-solution-bytes";
    private const string ActivityLogText = "encrypted-activity-log-bytes";

    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record UploadedSubmission(UploadedContent Solution, UploadedContent ActivityLog);

    private sealed record SubmissionCreated(Guid SubmissionId);

    private sealed record SessionCreated(Guid SessionId, string OneTimeCode);

    private sealed record ReviewedSubmission(
        Guid Id,
        Guid StudentId,
        string StudentFirstName,
        string StudentLastName,
        string? StudentIndexNumber,
        string StudentEmail,
        string DeviceName,
        DateTime SubmittedAt,
        bool SubmittedAfterSessionEnded,
        long SolutionSizeBytes,
        string SolutionSha256,
        long ActivityLogSizeBytes,
        string ActivityLogSha256);

    private sealed record ReviewPage(ReviewedSubmission[] Items, int TotalCount);

    private sealed record SubmissionDownload(
        Guid SubmissionId,
        string SolutionUrl,
        string ActivityLogUrl,
        DateTime ExpiresAt,
        string SolutionSha256,
        string ActivityLogSha256);

    private sealed record Sitting(Guid SessionId, string ProfessorToken);

    private sealed record HandedIn(Guid StudentId, Guid SubmissionId);

    // Publishes an exam and opens a sitting that is already under way, keeping the professor's
    // token so the test can switch back to it after a student has submitted.
    private async Task<Sitting> OpenSessionAsync()
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
        Guid sessionId = (await session.Content.ReadFromJsonAsync<SessionCreated>())!.SessionId;

        return new Sitting(sessionId, professor.AccessToken);
    }

    // A student registers, authenticates with the credential on their machine, and hands in a
    // solution with its activity log - the whole student side, as the desktop client will do it.
    private async Task<HandedIn> SubmitAsStudentAsync(Guid sessionId)
    {
        Registration student = await RegisterStudentAsync(UniqueEmail());

        HttpClient.DefaultRequestHeaders.Authorization = null;
        HttpResponseMessage tokenResponse = await IssueDeviceTokenAsync(student.DeviceCredential);
        tokenResponse.EnsureSuccessStatusCode();
        Authenticate((await tokenResponse.Content.ReadFromJsonAsync<AccessTokens>())!.AccessToken);

        using var form = new MultipartFormDataContent();
        using var solution = new ByteArrayContent(Encoding.UTF8.GetBytes(SolutionText));
        using var activityLog = new ByteArrayContent(Encoding.UTF8.GetBytes(ActivityLogText));
        form.Add(solution, "solution", "solution.bin");
        form.Add(activityLog, "activityLog", "activity-log.bin");

        HttpResponseMessage upload = await HttpClient.PostAsync($"sessions/{sessionId}/submissions/content", form);
        upload.EnsureSuccessStatusCode();
        UploadedSubmission uploaded = (await upload.Content.ReadFromJsonAsync<UploadedSubmission>())!;

        HttpResponseMessage submitted = await HttpClient.PostAsJsonAsync(
            $"sessions/{sessionId}/submissions",
            new
            {
                solutionObjectKey = uploaded.Solution.ObjectKey,
                activityLogObjectKey = uploaded.ActivityLog.ObjectKey
            });
        submitted.EnsureSuccessStatusCode();

        SubmissionCreated created = (await submitted.Content.ReadFromJsonAsync<SubmissionCreated>())!;

        return new HandedIn(student.UserId, created.SubmissionId);
    }

    [Fact]
    public async Task AProfessor_Should_SeeWhoSubmittedAndDownloadBothFiles()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();
        HandedIn handedIn = await SubmitAsStudentAsync(sitting.SessionId);

        Authenticate(sitting.ProfessorToken);

        // Act
        HttpResponseMessage list = await HttpClient.GetAsync($"sessions/{sitting.SessionId}/submissions");
        HttpResponseMessage download = await HttpClient.GetAsync($"submissions/{handedIn.SubmissionId}/download");

        // Assert - who handed in what, and from which machine.
        list.EnsureSuccessStatusCode();
        ReviewPage page = (await list.Content.ReadFromJsonAsync<ReviewPage>())!;
        page.TotalCount.ShouldBe(1);

        ReviewedSubmission reviewed = page.Items.Single();
        reviewed.Id.ShouldBe(handedIn.SubmissionId);
        reviewed.StudentId.ShouldBe(handedIn.StudentId);
        reviewed.DeviceName.ShouldBe("Test laptop");
        reviewed.SubmittedAfterSessionEnded.ShouldBeFalse();

        // Assert - both files come back exactly as the student's laptop sent them, fetched with no
        // API token at all, and match the digests the server recorded on arrival.
        download.EnsureSuccessStatusCode();
        SubmissionDownload urls = (await download.Content.ReadFromJsonAsync<SubmissionDownload>())!;

        using var anonymous = new HttpClient();
        byte[] solution = await anonymous.GetByteArrayAsync(urls.SolutionUrl);
        byte[] activityLog = await anonymous.GetByteArrayAsync(urls.ActivityLogUrl);

        Encoding.UTF8.GetString(solution).ShouldBe(SolutionText);
        Encoding.UTF8.GetString(activityLog).ShouldBe(ActivityLogText);
        Convert.ToHexStringLower(SHA256.HashData(solution)).ShouldBe(urls.SolutionSha256);
        Convert.ToHexStringLower(SHA256.HashData(activityLog)).ShouldBe(urls.ActivityLogSha256);
    }

    // Another professor is told the sitting and the submission do not exist, not that they are
    // forbidden, so ids cannot be probed.
    [Fact]
    public async Task AnotherProfessor_Should_GetNotFound_ForTheListAndTheDownload()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();
        HandedIn handedIn = await SubmitAsStudentAsync(sitting.SessionId);

        (Guid _, AccessTokens otherProfessor) = await RegisterAndLoginProfessorAsync();
        Authenticate(otherProfessor.AccessToken);

        // Act
        HttpResponseMessage list = await HttpClient.GetAsync($"sessions/{sitting.SessionId}/submissions");
        HttpResponseMessage download = await HttpClient.GetAsync($"submissions/{handedIn.SubmissionId}/download");

        // Assert
        list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Reviewing is a professor permission; a student cannot read even the sitting they sat.
    [Fact]
    public async Task AStudent_Should_BeForbiddenFromReviewing()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();
        HandedIn handedIn = await SubmitAsStudentAsync(sitting.SessionId);

        // Act - still authenticated as the student who just submitted.
        HttpResponseMessage list = await HttpClient.GetAsync($"sessions/{sitting.SessionId}/submissions");
        HttpResponseMessage download = await HttpClient.GetAsync($"submissions/{handedIn.SubmissionId}/download");

        // Assert
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        download.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Review_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();
        HttpClient.DefaultRequestHeaders.Authorization = null;

        // Act
        HttpResponseMessage list = await HttpClient.GetAsync($"sessions/{sitting.SessionId}/submissions");

        // Assert
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
