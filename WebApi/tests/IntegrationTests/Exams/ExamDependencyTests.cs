using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace IntegrationTests.Exams;

// Dependencies are two-phase like exam files, but the bytes go straight to MinIO on a presigned URL
// rather than through the API. These tests upload exactly that way - with a client carrying no
// bearer token, which knows nothing about the API - and then commit the key.
public sealed class ExamDependencyTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record UploadTicket(string ObjectKey, string UploadUrl, DateTime ExpiresAt);

    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record ExamResponse(Guid Id, string Status, ExamDependencyResponse[] Dependencies);

    private sealed record ExamDependencyResponse(
        Guid Id,
        string Name,
        string Version,
        string ContentType,
        long SizeBytes);

    private async Task<Guid> CreateExamAsync()
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Compilers",
            description = "Write a recursive descent parser.",
            subject = "Compiler Construction"
        });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<HttpResponseMessage> RequestUploadUrlAsync(Guid examId) =>
        await HttpClient.PostAsync($"exams/{examId}/dependencies/upload-url", content: null);

    private static async Task PutAsync(string uploadUrl, string content, string contentType)
    {
        using var anonymous = new HttpClient();
        using var body = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        HttpResponseMessage response = await anonymous.PutAsync(new Uri(uploadUrl), body);
        response.EnsureSuccessStatusCode();
    }

    private async Task<UploadTicket> UploadDependencyAsync(
        Guid examId,
        string content,
        string contentType = "application/gzip")
    {
        HttpResponseMessage ticketResponse = await RequestUploadUrlAsync(examId);
        ticketResponse.EnsureSuccessStatusCode();

        UploadTicket ticket = (await ticketResponse.Content.ReadFromJsonAsync<UploadTicket>())!;
        await PutAsync(ticket.UploadUrl, content, contentType);

        return ticket;
    }

    private async Task<UploadedContent> UploadExamFileAsync(Guid examId, string content)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "task.txt");

        HttpResponseMessage upload = await HttpClient.PostAsync($"exams/{examId}/files/content", form);
        upload.EnsureSuccessStatusCode();

        return (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;
    }

    private async Task<HttpResponseMessage> CommitAsync(
        Guid examId,
        string objectKey,
        string name = "GCC",
        string version = "13.2.0") =>
        await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/dependencies",
            new { objectKey, name, version });

    private async Task AuthenticateAsProfessorAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginProfessorAsync();
        Authenticate(tokens.AccessToken);
    }

    // The key is minted by the server and the signature is bound to it, so a caller cannot direct
    // the upload anywhere else.
    [Fact]
    public async Task UploadUrl_Should_BeIssuedForAServerMintedKeyAndCreateNoRow()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        // Act
        HttpResponseMessage response = await RequestUploadUrlAsync(examId);

        // Assert
        response.EnsureSuccessStatusCode();
        UploadTicket ticket = (await response.Content.ReadFromJsonAsync<UploadTicket>())!;
        ticket.ObjectKey.ShouldStartWith($"exams/{examId}/dependencies/");
        ticket.UploadUrl.ShouldContain(ticket.ObjectKey);
        ticket.ExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);

        HttpResponseMessage exam = await HttpClient.GetAsync($"exams/{examId}");
        ExamResponse body = (await exam.Content.ReadFromJsonAsync<ExamResponse>())!;
        body.Dependencies.ShouldBeEmpty();
    }

    // The bytes never touch the API: this PUT goes to MinIO with no credentials at all.
    [Fact]
    public async Task Commit_Should_RecordTheDependency_AfterAnAnonymousDirectUpload()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        UploadTicket ticket = await UploadDependencyAsync(examId, "fake-toolchain-bytes");

        // Act
        HttpResponseMessage commit = await CommitAsync(examId, ticket.ObjectKey);

        // Assert
        commit.EnsureSuccessStatusCode();

        HttpResponseMessage exam = await HttpClient.GetAsync($"exams/{examId}");
        ExamResponse body = (await exam.Content.ReadFromJsonAsync<ExamResponse>())!;
        body.Dependencies.Length.ShouldBe(1);
        body.Dependencies[0].Name.ShouldBe("GCC");
        body.Dependencies[0].Version.ShouldBe("13.2.0");

        // Size and content type are read back from the store, never taken from the request.
        body.Dependencies[0].SizeBytes.ShouldBe("fake-toolchain-bytes".Length);
        body.Dependencies[0].ContentType.ShouldBe("application/gzip");
    }

    // The guarantee the two-phase split exists for still holds even though the API never saw the
    // bytes: a key nothing was uploaded under cannot get into the database.
    [Fact]
    public async Task Commit_Should_BeRejected_WhenTheUploadNeverHappened()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        HttpResponseMessage ticketResponse = await RequestUploadUrlAsync(examId);
        UploadTicket ticket = (await ticketResponse.Content.ReadFromJsonAsync<UploadTicket>())!;

        // Act - commit the key without ever PUTting to it.
        HttpResponseMessage response = await CommitAsync(examId, ticket.ObjectKey);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // Files and dependencies live under separate prefixes precisely so that this cannot work.
    [Fact]
    public async Task Commit_Should_BeRejected_WhenTheKeyIsAnExamFileKey()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        UploadedContent file = await UploadExamFileAsync(examId, "abc");

        // Act
        HttpResponseMessage response = await CommitAsync(examId, file.ObjectKey);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Commit_Should_ReturnConflict_WhenTheSameNameAndVersionIsAddedTwice()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        UploadTicket first = await UploadDependencyAsync(examId, "abc");
        await CommitAsync(examId, first.ObjectKey);

        UploadTicket second = await UploadDependencyAsync(examId, "abcd");

        // Act
        HttpResponseMessage response = await CommitAsync(examId, second.ObjectKey);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Commit_Should_ReturnConflict_WhenTheSameObjectIsCommittedTwice()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        UploadTicket ticket = await UploadDependencyAsync(examId, "abc");

        await CommitAsync(examId, ticket.ObjectKey);

        // Act
        HttpResponseMessage response = await CommitAsync(examId, ticket.ObjectKey, version: "14.0.0");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Commit_Should_ReturnBadRequest_WhenVersionContainsWhitespace()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();
        UploadTicket ticket = await UploadDependencyAsync(examId, "abc");

        // Act
        HttpResponseMessage response = await CommitAsync(examId, ticket.ObjectKey, version: "13.2 beta");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadUrl_Should_ReturnConflict_OnceTheExamIsPublished()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        UploadedContent file = await UploadExamFileAsync(examId, "abc");
        HttpResponseMessage commit = await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/files",
            new { objectKey = file.ObjectKey, fileName = "task.txt", sha256 = file.Sha256 });
        commit.EnsureSuccessStatusCode();

        HttpResponseMessage publish = await HttpClient.PatchAsync($"exams/{examId}/publish", content: null);
        publish.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Act
        HttpResponseMessage response = await RequestUploadUrlAsync(examId);

        // Assert - authoring is closed, so no new upload can even be authorised.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UploadUrl_Should_ReturnForbidden_ForAStudent()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await RequestUploadUrlAsync(examId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadUrl_Should_ReturnNotFound_ForAnotherProfessorsExam()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync();

        // A second professor, who owns nothing.
        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await RequestUploadUrlAsync(examId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
