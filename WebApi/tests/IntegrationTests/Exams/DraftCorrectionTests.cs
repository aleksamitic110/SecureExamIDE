using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace IntegrationTests.Exams;

// While an exam is a draft its professor can correct it - change its details, take back a file or
// a dependency, or throw the whole draft away - and every removal reaches object storage, not just
// the database. Once published, all of that is refused.
public sealed class DraftCorrectionTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record UploadTicket(string ObjectKey, string UploadUrl, DateTime ExpiresAt);

    private sealed record FileItem(Guid Id, string FileName);

    private sealed record DependencyItem(Guid Id, string Name);

    private sealed record OwnerView(
        Guid Id,
        string Title,
        string Description,
        string Subject,
        FileItem[] Files,
        DependencyItem[] Dependencies);

    private sealed record MyExamsPage(int TotalCount);

    private sealed record Draft(Guid ExamId, string FileKey, string DependencyKey);

    private async Task AuthenticateAsProfessorAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginProfessorAsync();
        Authenticate(tokens.AccessToken);
    }

    // A draft with one task file and one dependency, both uploaded the real way.
    private async Task<Draft> CreateDraftAsync()
    {
        HttpResponseMessage created = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Compilers",
            description = "Write a parser.",
            subject = "Compiler Construction"
        });

        created.EnsureSuccessStatusCode();
        Guid examId = await created.Content.ReadFromJsonAsync<Guid>();

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Task 1."));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(fileContent, "file", "task.md");

        HttpResponseMessage upload = await HttpClient.PostAsync($"exams/{examId}/files/content", form);
        upload.EnsureSuccessStatusCode();
        UploadedContent file = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        (await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/files",
            new { objectKey = file.ObjectKey, fileName = "task.md" })).EnsureSuccessStatusCode();

        HttpResponseMessage ticketResponse = await HttpClient.PostAsync(
            $"exams/{examId}/dependencies/upload-url", content: null);
        ticketResponse.EnsureSuccessStatusCode();
        UploadTicket ticket = (await ticketResponse.Content.ReadFromJsonAsync<UploadTicket>())!;

        using (var anonymous = new HttpClient())
        using (var body = new ByteArrayContent(Encoding.UTF8.GetBytes("pretend-toolchain")))
        {
            body.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            (await anonymous.PutAsync(new Uri(ticket.UploadUrl), body)).EnsureSuccessStatusCode();
        }

        (await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/dependencies",
            new { objectKey = ticket.ObjectKey, name = "GCC", version = "14.2.0" })).EnsureSuccessStatusCode();

        return new Draft(examId, file.ObjectKey, ticket.ObjectKey);
    }

    private async Task<OwnerView> GetExamAsync(Guid examId) =>
        (await HttpClient.GetFromJsonAsync<OwnerView>($"exams/{examId}"))!;

    [Fact]
    public async Task Update_Should_ChangeOnlyTheFieldsSent()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Draft draft = await CreateDraftAsync();

        // Act
        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            $"exams/{draft.ExamId}", new { title = "Compilers - final" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        OwnerView exam = await GetExamAsync(draft.ExamId);
        exam.Title.ShouldBe("Compilers - final");
        exam.Description.ShouldBe("Write a parser.");
        exam.Subject.ShouldBe("Compiler Construction");
    }

    [Fact]
    public async Task Update_Should_ReturnBadRequest_WhenNothingIsSent()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Draft draft = await CreateDraftAsync();

        // Act
        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync($"exams/{draft.ExamId}", new { });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveFile_Should_DeleteTheRowAndTheObjectInStorage()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Draft draft = await CreateDraftAsync();
        Guid fileId = (await GetExamAsync(draft.ExamId)).Files.Single().Id;

        (await StorageContainsAsync(draft.FileKey)).ShouldBeTrue();

        // Act
        HttpResponseMessage response = await HttpClient.DeleteAsync($"exams/{draft.ExamId}/files/{fileId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetExamAsync(draft.ExamId)).Files.ShouldBeEmpty();
        (await StorageContainsAsync(draft.FileKey)).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveDependency_Should_DeleteTheRowAndTheObjectInStorage()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Draft draft = await CreateDraftAsync();
        Guid dependencyId = (await GetExamAsync(draft.ExamId)).Dependencies.Single().Id;

        // Act
        HttpResponseMessage response = await HttpClient.DeleteAsync(
            $"exams/{draft.ExamId}/dependencies/{dependencyId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetExamAsync(draft.ExamId)).Dependencies.ShouldBeEmpty();
        (await StorageContainsAsync(draft.DependencyKey)).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteExam_Should_RemoveTheDraftAndEveryObjectItHeld()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Draft draft = await CreateDraftAsync();

        // Act
        HttpResponseMessage response = await HttpClient.DeleteAsync($"exams/{draft.ExamId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await HttpClient.GetAsync($"exams/{draft.ExamId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await HttpClient.GetFromJsonAsync<MyExamsPage>("exams/mine"))!.TotalCount.ShouldBe(0);
        (await StorageContainsAsync(draft.FileKey)).ShouldBeFalse();
        (await StorageContainsAsync(draft.DependencyKey)).ShouldBeFalse();
    }

    // After publishing nothing may change: students may have downloaded it and sittings are
    // sealed from its files. Every correction is refused and every object stays where it was.
    [Fact]
    public async Task APublishedExam_Should_RefuseEveryCorrection()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Draft draft = await CreateDraftAsync();
        OwnerView before = await GetExamAsync(draft.ExamId);

        (await HttpClient.PatchAsync($"exams/{draft.ExamId}/publish", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Act
        HttpResponseMessage update = await HttpClient.PatchAsJsonAsync($"exams/{draft.ExamId}", new { title = "Too late" });
        HttpResponseMessage removeFile = await HttpClient.DeleteAsync($"exams/{draft.ExamId}/files/{before.Files.Single().Id}");
        HttpResponseMessage removeDependency = await HttpClient.DeleteAsync(
            $"exams/{draft.ExamId}/dependencies/{before.Dependencies.Single().Id}");
        HttpResponseMessage delete = await HttpClient.DeleteAsync($"exams/{draft.ExamId}");

        // Assert
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        removeFile.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        removeDependency.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await GetExamAsync(draft.ExamId)).Title.ShouldBe("Compilers");
        (await StorageContainsAsync(draft.FileKey)).ShouldBeTrue();
        (await StorageContainsAsync(draft.DependencyKey)).ShouldBeTrue();
    }

    // Reported as missing rather than forbidden, and the draft is left untouched.
    [Fact]
    public async Task AnotherProfessor_Should_GetNotFound_AndChangeNothing()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Draft draft = await CreateDraftAsync();

        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage update = await HttpClient.PatchAsJsonAsync($"exams/{draft.ExamId}", new { title = "Mine now" });
        HttpResponseMessage delete = await HttpClient.DeleteAsync($"exams/{draft.ExamId}");

        // Assert
        update.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StorageContainsAsync(draft.FileKey)).ShouldBeTrue();
    }

    [Fact]
    public async Task AStudent_Should_BeForbiddenFromCorrectingAnExam()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Draft draft = await CreateDraftAsync();

        (Guid _, AccessTokens student) = await RegisterAndLoginAsync();
        Authenticate(student.AccessToken);

        // Act
        HttpResponseMessage response = await HttpClient.DeleteAsync($"exams/{draft.ExamId}");

        // Assert - exams:manage is a professor-only permission.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
