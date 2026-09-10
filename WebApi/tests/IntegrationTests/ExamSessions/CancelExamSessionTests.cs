using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace IntegrationTests.ExamSessions;

// Cancelling a sitting takes it away from students - it drops out of their list, its package can
// no longer be fetched, and no new work is accepted - while anything already handed in stays with
// the professor.
public sealed class CancelExamSessionTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record UploadedSubmission(UploadedContent Solution, UploadedContent ActivityLog);

    private sealed record SessionCreated(Guid SessionId, string OneTimeCode);

    private sealed record SessionItem(Guid Id);

    private sealed record SessionPage(SessionItem[] Items, int TotalCount);

    private sealed record SubmissionPage(int TotalCount);

    private sealed record Sitting(Guid ExamId, Guid SessionId, string ProfessorToken);

    // Publishes an exam and opens a sitting that is already under way, keeping the professor's
    // token so the test can switch back to it.
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

        (await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/files",
            new { objectKey = uploaded.ObjectKey, fileName = "task.txt" })).EnsureSuccessStatusCode();

        HttpResponseMessage publish = await HttpClient.PatchAsync($"exams/{examId}/publish", content: null);
        publish.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage session = await HttpClient.PostAsJsonAsync($"exams/{examId}/sessions", new
        {
            startsAt = DateTime.UtcNow.AddHours(-1),
            endsAt = DateTime.UtcNow.AddHours(2)
        });

        session.EnsureSuccessStatusCode();
        Guid sessionId = (await session.Content.ReadFromJsonAsync<SessionCreated>())!.SessionId;

        return new Sitting(examId, sessionId, professor.AccessToken);
    }

    // A student authenticated the way the desktop client is: with the credential on the machine.
    private async Task<string> AuthenticateAsStudentDeviceAsync()
    {
        Registration student = await RegisterStudentAsync(UniqueEmail());

        HttpClient.DefaultRequestHeaders.Authorization = null;
        HttpResponseMessage tokenResponse = await IssueDeviceTokenAsync(student.DeviceCredential);
        tokenResponse.EnsureSuccessStatusCode();

        string token = (await tokenResponse.Content.ReadFromJsonAsync<AccessTokens>())!.AccessToken;
        Authenticate(token);

        return token;
    }

    // Builds and disposes the multipart body in one place, so no caller has to own it.
    private async Task<HttpResponseMessage> PostSubmissionContentAsync(Guid sessionId)
    {
        using var form = new MultipartFormDataContent();
        using var solution = new ByteArrayContent(Encoding.UTF8.GetBytes("encrypted-solution-bytes"));
        using var activityLog = new ByteArrayContent(Encoding.UTF8.GetBytes("encrypted-activity-log-bytes"));
        form.Add(solution, "solution", "solution.bin");
        form.Add(activityLog, "activityLog", "activity-log.bin");

        return await HttpClient.PostAsync($"sessions/{sessionId}/submissions/content", form);
    }

    private async Task SubmitAsync(Guid sessionId)
    {
        HttpResponseMessage upload = await PostSubmissionContentAsync(sessionId);
        upload.EnsureSuccessStatusCode();
        UploadedSubmission uploaded = (await upload.Content.ReadFromJsonAsync<UploadedSubmission>())!;

        (await HttpClient.PostAsJsonAsync(
            $"sessions/{sessionId}/submissions",
            new
            {
                solutionObjectKey = uploaded.Solution.ObjectKey,
                activityLogObjectKey = uploaded.ActivityLog.ObjectKey
            })).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> CancelAsync(Guid sessionId) =>
        await HttpClient.PatchAsync($"sessions/{sessionId}/cancel", content: null);

    [Fact]
    public async Task AProfessor_Should_CancelASitting_AndTakeItAwayFromStudents()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();
        string studentToken = await AuthenticateAsStudentDeviceAsync();

        // Act
        Authenticate(sitting.ProfessorToken);
        HttpResponseMessage cancelled = await CancelAsync(sitting.SessionId);

        // Assert
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Authenticate(studentToken);

        // Gone from the student's list of sittings for the exam...
        SessionPage sessions = (await HttpClient.GetFromJsonAsync<SessionPage>($"exams/{sitting.ExamId}/sessions"))!;
        sessions.Items.ShouldNotContain(s => s.Id == sitting.SessionId);

        // ...its package can no longer be fetched...
        HttpResponseMessage package = await HttpClient.GetAsync($"sessions/{sitting.SessionId}/package");
        package.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // ...and no new work is accepted for it.
        HttpResponseMessage upload = await PostSubmissionContentAsync(sitting.SessionId);
        upload.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // A submission is evidence: cancelling the sitting afterwards does not take it away from the
    // professor.
    [Fact]
    public async Task WorkHandedInBeforeTheCancellation_Should_StayReviewable()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();
        await AuthenticateAsStudentDeviceAsync();
        await SubmitAsync(sitting.SessionId);

        Authenticate(sitting.ProfessorToken);

        // Act
        HttpResponseMessage cancelled = await CancelAsync(sitting.SessionId);

        // Assert
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        SubmissionPage submissions =
            (await HttpClient.GetFromJsonAsync<SubmissionPage>($"sessions/{sitting.SessionId}/submissions"))!;
        submissions.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_Should_ReturnConflict_WhenTheSittingIsAlreadyCancelled()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();
        (await CancelAsync(sitting.SessionId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Act
        HttpResponseMessage response = await CancelAsync(sitting.SessionId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // Reported as missing, not forbidden - and the sitting stays open, which the owner then proves
    // by cancelling it themselves.
    [Fact]
    public async Task AnotherProfessor_Should_GetNotFound_AndLeaveTheSittingOpen()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();

        (Guid _, AccessTokens otherProfessor) = await RegisterAndLoginProfessorAsync();
        Authenticate(otherProfessor.AccessToken);

        // Act
        HttpResponseMessage response = await CancelAsync(sitting.SessionId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        Authenticate(sitting.ProfessorToken);
        (await CancelAsync(sitting.SessionId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AStudent_Should_BeForbiddenFromCancelling()
    {
        // Arrange
        Sitting sitting = await OpenSessionAsync();
        await AuthenticateAsStudentDeviceAsync();

        // Act
        HttpResponseMessage response = await CancelAsync(sitting.SessionId);

        // Assert - exams:manage is a professor-only permission.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
