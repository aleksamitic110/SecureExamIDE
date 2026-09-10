using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace IntegrationTests.Exams;

// Publishing closes an exam to further authoring, so these tests care as much about what stops
// working afterwards as about the status flipping.
public sealed class PublishExamTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record ExamResponse(Guid Id, string Status, DateTime? PublishedAt);

    private async Task<Guid> CreateExamAsync()
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Operating Systems",
            description = "Implement a scheduler.",
            subject = "Operating Systems"
        });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task AddFileAsync(Guid examId)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("abc"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "task.txt");

        HttpResponseMessage upload = await HttpClient.PostAsync($"exams/{examId}/files/content", form);
        upload.EnsureSuccessStatusCode();

        UploadedContent uploaded = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        HttpResponseMessage commit = await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/files",
            new { objectKey = uploaded.ObjectKey, fileName = "task.txt", sha256 = uploaded.Sha256 });

        commit.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> PublishAsync(Guid examId) =>
        await HttpClient.PatchAsync($"exams/{examId}/publish", content: null);

    private async Task AuthenticateAsProfessorAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginProfessorAsync();
        Authenticate(tokens.AccessToken);
    }

    [Fact]
    public async Task Publish_Should_MoveTheExamToPublishedAndStampTheTime()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        await AddFileAsync(examId);

        // Act
        HttpResponseMessage response = await PublishAsync(examId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage exam = await HttpClient.GetAsync($"exams/{examId}");
        ExamResponse body = (await exam.Content.ReadFromJsonAsync<ExamResponse>())!;
        body.Status.ShouldBe("Published");
        body.PublishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Publish_Should_ReturnConflict_WhenTheExamHasNoFiles()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        // Act
        HttpResponseMessage response = await PublishAsync(examId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        HttpResponseMessage exam = await HttpClient.GetAsync($"exams/{examId}");
        ExamResponse body = (await exam.Content.ReadFromJsonAsync<ExamResponse>())!;
        body.Status.ShouldBe("Draft");
    }

    [Fact]
    public async Task Publish_Should_ReturnConflict_WhenCalledTwice()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        await AddFileAsync(examId);

        await PublishAsync(examId);

        // Act
        HttpResponseMessage response = await PublishAsync(examId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // Authoring is closed once the exam is out: the upload endpoints check the draft status for
    // exactly this reason.
    [Fact]
    public async Task Publish_Should_CloseTheExamToFurtherUploads()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        await AddFileAsync(examId);
        await PublishAsync(examId);

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("late"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "late.txt");

        // Act
        HttpResponseMessage response = await HttpClient.PostAsync($"exams/{examId}/files/content", form);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Publish_Should_ReturnNotFound_ForAnotherProfessorsExam()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        await AddFileAsync(examId);

        // A second professor, who owns nothing.
        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await PublishAsync(examId);

        // Assert - reported as missing, not forbidden, so exam ids cannot be probed.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publish_Should_ReturnForbidden_ForAStudent()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        await AddFileAsync(examId);

        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await PublishAsync(examId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Publish_Should_ReturnBadRequest_WhenExamIdIsEmpty()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await PublishAsync(Guid.Empty);

        // Assert - the void-command validation pipeline, same as RevokeDevice.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
