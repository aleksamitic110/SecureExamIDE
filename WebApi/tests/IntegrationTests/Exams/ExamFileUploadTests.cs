using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace IntegrationTests.Exams;

// Exercises the two-phase upload against a real MinIO container: phase one writes bytes and
// nothing else, phase two records them only after proving they are there.
public sealed class ExamFileUploadTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record ExamResponse(
        Guid Id,
        string Title,
        string Subject,
        string Status,
        ExamFileResponse[] Files);

    private sealed record ExamFileResponse(
        Guid Id,
        string FileName,
        string ContentType,
        long SizeBytes,
        string Sha256);

    private async Task<Guid> CreateExamAsync()
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Algorithms final",
            description = "Implement a balanced tree.",
            subject = "Algorithms and Data Structures"
        });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<HttpResponseMessage> UploadAsync(
        Guid examId,
        string content,
        string fileName = "task.txt",
        string contentType = "text/plain")
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        return await HttpClient.PostAsync($"exams/{examId}/files/content", form);
    }

    private async Task<HttpResponseMessage> CommitAsync(
        Guid examId,
        string objectKey,
        string sha256,
        string fileName = "task.txt") =>
        await HttpClient.PostAsJsonAsync($"exams/{examId}/files", new { objectKey, fileName, sha256 });

    private async Task AuthenticateAsProfessorAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginProfessorAsync();
        Authenticate(tokens.AccessToken);
    }

    [Fact]
    public async Task Upload_Should_StoreContentAndReturnTheServersOwnHash()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        // Act
        HttpResponseMessage response = await UploadAsync(examId, "abc");

        // Assert
        response.EnsureSuccessStatusCode();
        UploadedContent? uploaded = await response.Content.ReadFromJsonAsync<UploadedContent>();
        uploaded!.ObjectKey.ShouldStartWith($"exams/{examId}/files/");
        uploaded.SizeBytes.ShouldBe(3);
        uploaded.Sha256.ShouldBe(Sha256Of("abc"));
    }

    [Fact]
    public async Task Upload_Should_NotCreateAFileRow()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        // Act
        await UploadAsync(examId, "abc");

        // Assert - the exam still lists no files until the commit call happens.
        HttpResponseMessage exam = await HttpClient.GetAsync($"exams/{examId}");
        ExamResponse? body = await exam.Content.ReadFromJsonAsync<ExamResponse>();
        body!.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_Should_RecordTheFile_AfterContentHasBeenUploaded()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        HttpResponseMessage upload = await UploadAsync(examId, "abc", contentType: "application/pdf");
        UploadedContent uploaded = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        // Act
        HttpResponseMessage commit = await CommitAsync(examId, uploaded.ObjectKey, uploaded.Sha256, "task.pdf");

        // Assert
        commit.EnsureSuccessStatusCode();

        HttpResponseMessage exam = await HttpClient.GetAsync($"exams/{examId}");
        ExamResponse body = (await exam.Content.ReadFromJsonAsync<ExamResponse>())!;
        body.Files.Length.ShouldBe(1);
        body.Files[0].FileName.ShouldBe("task.pdf");
        body.Files[0].SizeBytes.ShouldBe(3);
        body.Files[0].ContentType.ShouldBe("application/pdf");
        body.Files[0].Sha256.ShouldBe(Sha256Of("abc"));
    }

    // The guarantee this whole design exists to provide: a key that was never uploaded cannot get
    // into the database.
    [Fact]
    public async Task Commit_Should_BeRejected_WhenNothingWasEverUploadedUnderThatKey()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        string inventedKey = $"exams/{examId}/files/{Guid.NewGuid()}";

        // Act
        HttpResponseMessage response = await CommitAsync(examId, inventedKey, Sha256Of("abc"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        HttpResponseMessage exam = await HttpClient.GetAsync($"exams/{examId}");
        ExamResponse body = (await exam.Content.ReadFromJsonAsync<ExamResponse>())!;
        body.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_Should_BeRejected_WhenTheKeyBelongsToAnotherExam()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid firstExam = await CreateExamAsync();
        Guid secondExam = await CreateExamAsync();

        HttpResponseMessage upload = await UploadAsync(firstExam, "abc");
        UploadedContent uploaded = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        // Act - try to attach the first exam's stored object to the second exam.
        HttpResponseMessage response = await CommitAsync(secondExam, uploaded.ObjectKey, uploaded.Sha256);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Commit_Should_ReturnConflict_WhenTheSameUploadIsCommittedTwice()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        HttpResponseMessage upload = await UploadAsync(examId, "abc");
        UploadedContent uploaded = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        await CommitAsync(examId, uploaded.ObjectKey, uploaded.Sha256);

        // Act
        HttpResponseMessage response = await CommitAsync(examId, uploaded.ObjectKey, uploaded.Sha256);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Commit_Should_ReturnBadRequest_WhenHashIsNotLowercaseHex()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        HttpResponseMessage upload = await UploadAsync(examId, "abc");
        UploadedContent uploaded = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        // Act
        HttpResponseMessage response = await CommitAsync(examId, uploaded.ObjectKey, "not-a-hash");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_Should_ReturnNotFound_WhenExamBelongsToAnotherProfessor()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        // A second professor, who owns nothing.
        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await UploadAsync(examId, "abc");

        // Assert - reported as missing, not forbidden, so exam ids cannot be probed.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Exams_Should_ReturnForbidden_ForAStudent()
    {
        // Arrange
        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Not allowed",
            description = "Students cannot author exams.",
            subject = "Algorithms"
        });

        // Assert - exams:manage is a professor-only permission.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Exams_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Anonymous",
            description = "No token supplied.",
            subject = "Algorithms"
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static string Sha256Of(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
