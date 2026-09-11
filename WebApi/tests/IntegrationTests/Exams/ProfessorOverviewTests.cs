using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace IntegrationTests.Exams;

// A professor's own view of their work: every exam they have made, drafts included, and every
// sitting they have scheduled, cancelled ones included - and nothing that belongs to anyone else.
public sealed class ProfessorOverviewTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record UploadedSubmission(UploadedContent Solution, UploadedContent ActivityLog);

    private sealed record SessionCreated(Guid SessionId, string OneTimeCode);

    private sealed record MyExam(Guid Id, string Title, string Status, int FileCount, int SessionCount);

    private sealed record MyExamsPage(MyExam[] Items, int TotalCount);

    private sealed record MySession(Guid Id, Guid ExamId, string ExamTitle, bool IsCancelled, int SubmissionCount);

    private sealed record MySessionsPage(MySession[] Items, int TotalCount);

    private async Task<string> AuthenticateAsProfessorAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginProfessorAsync();
        Authenticate(tokens.AccessToken);

        return tokens.AccessToken;
    }

    // A draft with one task file in it.
    private async Task<Guid> CreateDraftAsync(string title)
    {
        HttpResponseMessage created = await HttpClient.PostAsJsonAsync("exams", new
        {
            title,
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

        return examId;
    }

    private async Task PublishAsync(Guid examId) =>
        (await HttpClient.PatchAsync($"exams/{examId}/publish", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

    private async Task<Guid> OpenSittingAsync(Guid examId)
    {
        HttpResponseMessage session = await HttpClient.PostAsJsonAsync($"exams/{examId}/sessions", new
        {
            startsAt = DateTime.UtcNow.AddHours(-1),
            endsAt = DateTime.UtcNow.AddHours(2)
        });

        session.EnsureSuccessStatusCode();

        return (await session.Content.ReadFromJsonAsync<SessionCreated>())!.SessionId;
    }

    // A student hands in work for the sitting from their bound machine.
    private async Task SubmitAsStudentAsync(Guid sessionId)
    {
        Registration student = await RegisterStudentAsync(UniqueEmail());

        HttpClient.DefaultRequestHeaders.Authorization = null;
        HttpResponseMessage tokenResponse = await IssueDeviceTokenAsync(student.DeviceCredential);
        tokenResponse.EnsureSuccessStatusCode();
        Authenticate((await tokenResponse.Content.ReadFromJsonAsync<AccessTokens>())!.AccessToken);

        using var form = new MultipartFormDataContent();
        using var solution = new ByteArrayContent(Encoding.UTF8.GetBytes("encrypted-solution-bytes"));
        using var activityLog = new ByteArrayContent(Encoding.UTF8.GetBytes("encrypted-activity-log-bytes"));
        form.Add(solution, "solution", "solution.bin");
        form.Add(activityLog, "activityLog", "activity-log.bin");

        HttpResponseMessage upload = await HttpClient.PostAsync($"sessions/{sessionId}/submissions/content", form);
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

    [Fact]
    public async Task MyExams_Should_ListDraftsAndPublishedExams_ButNotAnotherProfessors()
    {
        // Arrange - someone else's exam first, then the caller's own.
        await AuthenticateAsProfessorAsync();
        Guid theirs = await CreateDraftAsync("Someone else's exam");

        await AuthenticateAsProfessorAsync();
        Guid published = await CreateDraftAsync("Algorithms");
        await PublishAsync(published);
        Guid draft = await CreateDraftAsync("Compilers");

        // Act
        MyExamsPage all = (await HttpClient.GetFromJsonAsync<MyExamsPage>("exams/mine"))!;
        MyExamsPage drafts = (await HttpClient.GetFromJsonAsync<MyExamsPage>("exams/mine?status=Draft"))!;

        // Assert
        all.TotalCount.ShouldBe(2);
        all.Items.Select(e => e.Id).ShouldBe([draft, published]);
        all.Items.ShouldNotContain(e => e.Id == theirs);
        all.Items[0].Status.ShouldBe("Draft");
        all.Items[0].FileCount.ShouldBe(1);

        drafts.Items.Select(e => e.Id).ShouldBe([draft]);
    }

    [Fact]
    public async Task MyExams_Should_RejectAnUnknownStatus()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync("exams/mine?status=Deleted");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // A cancelled sitting used to vanish from every list; here it stays, marked as cancelled, next
    // to the count of work that arrived for the one that went ahead.
    [Fact]
    public async Task MySessions_Should_IncludeCancelledSittings_AndCountSubmissions()
    {
        // Arrange
        string professor = await AuthenticateAsProfessorAsync();
        Guid examId = await CreateDraftAsync("Compilers");
        await PublishAsync(examId);

        Guid heldSitting = await OpenSittingAsync(examId);
        Guid cancelledSitting = await OpenSittingAsync(examId);

        await SubmitAsStudentAsync(heldSitting);

        Authenticate(professor);
        (await HttpClient.PatchAsync($"sessions/{cancelledSitting}/cancel", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Act
        MySessionsPage page = (await HttpClient.GetFromJsonAsync<MySessionsPage>($"sessions/mine?examId={examId}"))!;

        // Assert
        page.TotalCount.ShouldBe(2);

        MySession held = page.Items.Single(s => s.Id == heldSitting);
        held.IsCancelled.ShouldBeFalse();
        held.SubmissionCount.ShouldBe(1);
        held.ExamTitle.ShouldBe("Compilers");

        MySession cancelled = page.Items.Single(s => s.Id == cancelledSitting);
        cancelled.IsCancelled.ShouldBeTrue();
        cancelled.SubmissionCount.ShouldBe(0);

        // The exam's own row counts both sittings.
        MyExamsPage exams = (await HttpClient.GetFromJsonAsync<MyExamsPage>("exams/mine"))!;
        exams.Items.Single(e => e.Id == examId).SessionCount.ShouldBe(2);
    }

    [Fact]
    public async Task AnotherProfessor_Should_SeeNoneOfIt()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateDraftAsync("Compilers");
        await PublishAsync(examId);
        await OpenSittingAsync(examId);

        await AuthenticateAsProfessorAsync();

        // Act
        MyExamsPage exams = (await HttpClient.GetFromJsonAsync<MyExamsPage>("exams/mine"))!;
        MySessionsPage sessions = (await HttpClient.GetFromJsonAsync<MySessionsPage>($"sessions/mine?examId={examId}"))!;

        // Assert
        exams.TotalCount.ShouldBe(0);
        sessions.TotalCount.ShouldBe(0);
    }

    // Both lists are professor views; a student gets 403 from the permission.
    [Fact]
    public async Task AStudent_Should_BeForbiddenFromBothLists()
    {
        // Arrange
        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage exams = await HttpClient.GetAsync("exams/mine");
        HttpResponseMessage sessions = await HttpClient.GetAsync("sessions/mine");

        // Assert
        exams.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        sessions.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
