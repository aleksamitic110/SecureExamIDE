using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Web.Api.Common.Crypto;

namespace IntegrationTests.ExamSessions;

// The end-to-end proof of the thesis's central claim: a professor seals a sitting, a student
// downloads the package from object storage with no privileged access, and the one-time code alone
// turns it back into the exam. Every step goes over real HTTP against real PostgreSQL and MinIO.
public sealed class ExamSessionTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const string TaskText = "Task 1: implement a recursive descent parser.";

    private static readonly JsonSerializerOptions HeaderJson = new(JsonSerializerDefaults.Web);

    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record SessionCreated(
        Guid SessionId,
        string OneTimeCode,
        DateTime StartsAt,
        DateTime EndsAt,
        long PackageSizeBytes,
        string PackageSha256);

    private sealed record SessionPackage(
        Guid SessionId,
        string PackageUrl,
        string HeaderUrl,
        DateTime ExpiresAt,
        long PackageSizeBytes,
        string PackageSha256);

    private sealed record SessionListItem(Guid Id, Guid ExamId, DateTime StartsAt, DateTime EndsAt);

    private sealed record SessionPage(SessionListItem[] Items, int Page, int PageSize, int TotalCount);

    private async Task<Guid> PublishExamWithTaskAsync(string subject)
    {
        HttpResponseMessage created = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Compilers",
            description = "Write a parser.",
            subject
        });

        created.EnsureSuccessStatusCode();
        Guid examId = await created.Content.ReadFromJsonAsync<Guid>();

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(TaskText));
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

        return examId;
    }

    private async Task<HttpResponseMessage> CreateSessionAsync(Guid examId, TimeSpan startsIn) =>
        await HttpClient.PostAsJsonAsync($"exams/{examId}/sessions", new
        {
            startsAt = DateTime.UtcNow.Add(startsIn),
            endsAt = DateTime.UtcNow.Add(startsIn).AddHours(3)
        });

    private async Task AuthenticateAsProfessorAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginProfessorAsync();
        Authenticate(tokens.AccessToken);
    }

    private async Task AuthenticateAsStudentAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);
    }

    [Fact]
    public async Task AStudent_Should_DownloadThePackageAndOpenItWithTheCode()
    {
        // Arrange - the professor seals a sitting and is shown the code once.
        await AuthenticateAsProfessorAsync();
        Guid examId = await PublishExamWithTaskAsync($"Subject {Guid.NewGuid()}");

        HttpResponseMessage created = await CreateSessionAsync(examId, TimeSpan.FromDays(7));
        created.EnsureSuccessStatusCode();
        SessionCreated session = (await created.Content.ReadFromJsonAsync<SessionCreated>())!;

        // The student never sees the professor's account.
        await AuthenticateAsStudentAsync();

        // Act - fetch the download links, then pull the bytes straight from object storage with no
        // credentials of any kind, the way a client at home would.
        HttpResponseMessage packageResponse = await HttpClient.GetAsync($"sessions/{session.SessionId}/package");
        packageResponse.EnsureSuccessStatusCode();
        SessionPackage package = (await packageResponse.Content.ReadFromJsonAsync<SessionPackage>())!;

        using var anonymous = new HttpClient();
        byte[] ciphertext = await anonymous.GetByteArrayAsync(package.PackageUrl);
        string headerJson = await anonymous.GetStringAsync(package.HeaderUrl);

        PackageHeader header = JsonSerializer.Deserialize<PackageHeader>(headerJson, HeaderJson)!;

        // Assert - what came off the wire is unreadable until the code is applied.
        Encoding.UTF8.GetString(ciphertext).ShouldNotContain("recursive descent");

        byte[] archive = PackageReader.Open(ciphertext, header, session.OneTimeCode);

        using var archiveStream = new MemoryStream(archive);
        using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        ZipArchiveEntry entry = zip.Entries.Single();
        entry.Name.ShouldBe("task.txt");

        using var reader = new StreamReader(await entry.OpenAsync(CancellationToken.None));
        (await reader.ReadToEndAsync()).ShouldBe(TaskText);
    }

    [Fact]
    public async Task TheWrongCode_Should_NotOpenThePackage()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await PublishExamWithTaskAsync($"Subject {Guid.NewGuid()}");

        HttpResponseMessage created = await CreateSessionAsync(examId, TimeSpan.FromDays(7));
        SessionCreated session = (await created.Content.ReadFromJsonAsync<SessionCreated>())!;

        await AuthenticateAsStudentAsync();

        HttpResponseMessage packageResponse = await HttpClient.GetAsync($"sessions/{session.SessionId}/package");
        SessionPackage package = (await packageResponse.Content.ReadFromJsonAsync<SessionPackage>())!;

        using var anonymous = new HttpClient();
        byte[] ciphertext = await anonymous.GetByteArrayAsync(package.PackageUrl);
        string headerJson = await anonymous.GetStringAsync(package.HeaderUrl);
        PackageHeader header = JsonSerializer.Deserialize<PackageHeader>(headerJson, HeaderJson)!;

        // Act & Assert - a guessed code fails on the authentication tag rather than yielding
        // something that looks like a damaged download.
        Should.Throw<System.Security.Cryptography.AuthenticationTagMismatchException>(() =>
            PackageReader.Open(ciphertext, header, "7QK2-M9XB-4TVA-0HRE-JW3N"));
    }

    [Fact]
    public async Task Sessions_Should_BeListedForAStudentAndPaged()
    {
        // Arrange
        string subject = $"Subject {Guid.NewGuid()}";
        await AuthenticateAsProfessorAsync();
        Guid examId = await PublishExamWithTaskAsync(subject);

        await CreateSessionAsync(examId, TimeSpan.FromDays(7));
        await CreateSessionAsync(examId, TimeSpan.FromDays(14));
        await CreateSessionAsync(examId, TimeSpan.FromDays(21));

        await AuthenticateAsStudentAsync();

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"exams/{examId}/sessions?page=1&pageSize=2");
        response.EnsureSuccessStatusCode();
        SessionPage page = (await response.Content.ReadFromJsonAsync<SessionPage>())!;

        // Assert
        page.TotalCount.ShouldBe(3);
        page.Items.Length.ShouldBe(2);
        page.Items[0].StartsAt.ShouldBeLessThan(page.Items[1].StartsAt);
    }

    [Fact]
    public async Task CreateSession_Should_ReturnConflict_WhenTheExamIsNotPublished()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();

        HttpResponseMessage created = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Still a draft",
            description = "Not published yet.",
            subject = $"Subject {Guid.NewGuid()}"
        });

        Guid examId = await created.Content.ReadFromJsonAsync<Guid>();

        // Act
        HttpResponseMessage response = await CreateSessionAsync(examId, TimeSpan.FromDays(7));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateSession_Should_ReturnForbidden_ForAStudent()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await PublishExamWithTaskAsync($"Subject {Guid.NewGuid()}");

        await AuthenticateAsStudentAsync();

        // Act
        HttpResponseMessage response = await CreateSessionAsync(examId, TimeSpan.FromDays(7));

        // Assert - sealing an exam is exams:manage, which students do not have.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateSession_Should_ReturnNotFound_ForAnotherProfessorsExam()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();
        Guid examId = await PublishExamWithTaskAsync($"Subject {Guid.NewGuid()}");

        // A second professor, who owns nothing.
        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await CreateSessionAsync(examId, TimeSpan.FromDays(7));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Package_Should_ReturnNotFound_ForAnUnknownSession()
    {
        // Arrange
        await AuthenticateAsStudentAsync();

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"sessions/{Guid.NewGuid()}/package");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Package_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"sessions/{Guid.NewGuid()}/package");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
