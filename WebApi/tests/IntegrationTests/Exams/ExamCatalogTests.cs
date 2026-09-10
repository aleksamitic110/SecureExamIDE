using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace IntegrationTests.Exams;

// Runs against real PostgreSQL, which is the point: the catalog query joins, filters on a value
// object and aggregates in correlated subqueries, and none of that is proven to translate to SQL
// by the in-memory provider the unit tests use.
//
// Every test filters on a subject unique to itself, because the catalog is global and the other
// tests in this assembly publish exams into the same database.
public sealed class ExamCatalogTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record UploadTicket(string ObjectKey, string UploadUrl, DateTime ExpiresAt);

    private sealed record CatalogPage(
        CatalogEntry[] Items,
        int Page,
        int PageSize,
        int TotalCount,
        bool HasNextPage,
        bool HasPreviousPage);

    private sealed record CatalogEntry(
        Guid Id,
        string Title,
        string Subject,
        DateTime? PublishedAt,
        string ProfessorFirstName,
        string ProfessorLastName,
        int FileCount,
        int DependencyCount,
        long TotalSizeBytes);

    private static string UniqueSubject() =>
        string.Create(CultureInfo.InvariantCulture, $"Subject {Guid.NewGuid()}");

    private async Task<Guid> CreateExamAsync(string title, string subject)
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("exams", new
        {
            title,
            description = "Description.",
            subject
        });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task AddFileAsync(Guid examId, string content = "abc")
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "task.txt");

        HttpResponseMessage upload = await HttpClient.PostAsync($"exams/{examId}/files/content", form);
        upload.EnsureSuccessStatusCode();

        UploadedContent uploaded = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        HttpResponseMessage commit = await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/files",
            new { objectKey = uploaded.ObjectKey, fileName = "task.txt" });

        commit.EnsureSuccessStatusCode();
    }

    // Dependencies go straight to storage on a presigned URL, so this uploads the way a client
    // would: request a ticket, PUT anonymously, then commit the key.
    private async Task AddDependencyAsync(Guid examId, string content = "dependency-bytes")
    {
        HttpResponseMessage ticketResponse = await HttpClient.PostAsync(
            $"exams/{examId}/dependencies/upload-url", content: null);
        ticketResponse.EnsureSuccessStatusCode();

        UploadTicket ticket = (await ticketResponse.Content.ReadFromJsonAsync<UploadTicket>())!;

        using (var anonymous = new HttpClient())
        {
            using var body = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
            body.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");

            HttpResponseMessage put = await anonymous.PutAsync(new Uri(ticket.UploadUrl), body);
            put.EnsureSuccessStatusCode();
        }

        HttpResponseMessage commit = await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/dependencies",
            new { objectKey = ticket.ObjectKey, name = "GCC", version = "13.2.0" });

        commit.EnsureSuccessStatusCode();
    }

    private async Task<Guid> PublishNewExamAsync(string title, string subject)
    {
        Guid examId = await CreateExamAsync(title, subject);
        await AddFileAsync(examId);

        HttpResponseMessage publish = await HttpClient.PatchAsync($"exams/{examId}/publish", content: null);
        publish.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return examId;
    }

    private async Task<CatalogPage> GetCatalogAsync(string subject, int? page = null, int? pageSize = null)
    {
        string url = $"exams?subject={Uri.EscapeDataString(subject)}";

        if (page is not null)
        {
            url += $"&page={page}";
        }

        if (pageSize is not null)
        {
            url += $"&pageSize={pageSize}";
        }

        HttpResponseMessage response = await HttpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CatalogPage>())!;
    }

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
    public async Task Catalog_Should_ShowPublishedExamsToAStudent()
    {
        // Arrange
        string subject = UniqueSubject();
        await AuthenticateAsProfessorAsync();
        Guid examId = await PublishNewExamAsync("Visible exam", subject);

        await AuthenticateAsStudentAsync();

        // Act
        CatalogPage page = await GetCatalogAsync(subject);

        // Assert
        page.TotalCount.ShouldBe(1);
        page.Items.Single().Id.ShouldBe(examId);
        page.Items.Single().Title.ShouldBe("Visible exam");
        page.Items.Single().PublishedAt.ShouldNotBeNull();
    }

    // A draft is the professor's private workspace; a student must not learn that it exists.
    [Fact]
    public async Task Catalog_Should_HideDrafts()
    {
        // Arrange
        string subject = UniqueSubject();
        await AuthenticateAsProfessorAsync();
        await CreateExamAsync("Unpublished exam", subject);

        await AuthenticateAsStudentAsync();

        // Act
        CatalogPage page = await GetCatalogAsync(subject);

        // Assert
        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Catalog_Should_ReturnOnePageAtATime()
    {
        // Arrange
        string subject = UniqueSubject();
        await AuthenticateAsProfessorAsync();

        for (int i = 0; i < 5; i++)
        {
            await PublishNewExamAsync($"Exam {i}", subject);
        }

        await AuthenticateAsStudentAsync();

        // Act
        CatalogPage first = await GetCatalogAsync(subject, page: 1, pageSize: 2);
        CatalogPage last = await GetCatalogAsync(subject, page: 3, pageSize: 2);

        // Assert
        first.Items.Length.ShouldBe(2);
        first.TotalCount.ShouldBe(5);
        first.HasNextPage.ShouldBeTrue();
        first.HasPreviousPage.ShouldBeFalse();

        last.Items.Length.ShouldBe(1);
        last.HasNextPage.ShouldBeFalse();
        last.HasPreviousPage.ShouldBeTrue();

        first.Items.Select(e => e.Id).ShouldNotContain(last.Items[0].Id);
    }

    // The cap is what stops a caller from asking the database for the entire catalog in one go.
    [Fact]
    public async Task Catalog_Should_CapThePageSize()
    {
        // Arrange
        string subject = UniqueSubject();
        await AuthenticateAsStudentAsync();

        // Act
        CatalogPage page = await GetCatalogAsync(subject, page: 1, pageSize: 100_000);

        // Assert
        page.PageSize.ShouldBe(100);
    }

    [Fact]
    public async Task Catalog_Should_ReportTheDownloadSizeAndWhoPublishedIt()
    {
        // Arrange
        string subject = UniqueSubject();
        await AuthenticateAsProfessorAsync();
        Guid examId = await CreateExamAsync("Sized exam", subject);
        await AddFileAsync(examId, "abc");
        await AddDependencyAsync(examId, "dependency-bytes");

        HttpResponseMessage publish = await HttpClient.PatchAsync($"exams/{examId}/publish", content: null);
        publish.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await AuthenticateAsStudentAsync();

        // Act
        CatalogPage page = await GetCatalogAsync(subject);

        // Assert
        CatalogEntry entry = page.Items.Single();
        entry.FileCount.ShouldBe(1);
        entry.DependencyCount.ShouldBe(1);
        entry.TotalSizeBytes.ShouldBe(3 + 16);
        entry.ProfessorFirstName.ShouldNotBeNullOrWhiteSpace();
        entry.ProfessorLastName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Catalog_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync("exams");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
