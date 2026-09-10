using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace IntegrationTests.Exams;

// The student's side of dependencies: the toolchains a professor attached to a published exam can
// be listed and fetched straight from storage, with no API token on the download itself, while a
// draft's dependencies stay invisible.
public sealed class DependencyDownloadTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const string Toolchain = "pretend-this-is-a-compiler-archive";

    private sealed record UploadTicket(string ObjectKey, string UploadUrl, DateTime ExpiresAt);

    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record DependencyItem(Guid Id, string Name, string Version, string ContentType, long SizeBytes);

    private sealed record DependencyPage(DependencyItem[] Items, int TotalCount);

    private sealed record OwnerExamView(DependencyItem[] Dependencies);

    private sealed record DependencyDownload(
        Guid DependencyId,
        string Name,
        string Version,
        string ContentType,
        long SizeBytes,
        string DownloadUrl,
        DateTime ExpiresAt);

    private sealed record PreparedExam(Guid ExamId, Guid DependencyId);

    // A professor builds an exam with one task file and one dependency uploaded the real way, and
    // publishes it only when asked, so a test can also look at a draft.
    private async Task<PreparedExam> PrepareExamAsync(bool publish)
    {
        (Guid _, AccessTokens professor) = await RegisterAndLoginProfessorAsync();
        Authenticate(professor.AccessToken);

        HttpResponseMessage created = await HttpClient.PostAsJsonAsync("exams", new
        {
            title = "Compilers",
            description = "Write a recursive descent parser.",
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
        UploadedContent file = (await upload.Content.ReadFromJsonAsync<UploadedContent>())!;

        (await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/files",
            new { objectKey = file.ObjectKey, fileName = "task.txt" })).EnsureSuccessStatusCode();

        HttpResponseMessage ticketResponse = await HttpClient.PostAsync(
            $"exams/{examId}/dependencies/upload-url", content: null);
        ticketResponse.EnsureSuccessStatusCode();
        UploadTicket ticket = (await ticketResponse.Content.ReadFromJsonAsync<UploadTicket>())!;

        using (var anonymous = new HttpClient())
        using (var body = new ByteArrayContent(Encoding.UTF8.GetBytes(Toolchain)))
        {
            body.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            (await anonymous.PutAsync(new Uri(ticket.UploadUrl), body)).EnsureSuccessStatusCode();
        }

        (await HttpClient.PostAsJsonAsync(
            $"exams/{examId}/dependencies",
            new { objectKey = ticket.ObjectKey, name = "GCC", version = "14.2.0" })).EnsureSuccessStatusCode();

        // The owner's view is the one place a draft's dependency ids can be read.
        OwnerExamView ownerView = (await HttpClient.GetFromJsonAsync<OwnerExamView>($"exams/{examId}"))!;

        if (publish)
        {
            HttpResponseMessage published = await HttpClient.PatchAsync($"exams/{examId}/publish", content: null);
            published.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return new PreparedExam(examId, ownerView.Dependencies.Single().Id);
    }

    private async Task AuthenticateAsStudentAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);
    }

    [Fact]
    public async Task AStudent_Should_ListAndDownloadAPublishedExamsDependency()
    {
        // Arrange
        PreparedExam exam = await PrepareExamAsync(publish: true);
        await AuthenticateAsStudentAsync();

        // Act
        HttpResponseMessage list = await HttpClient.GetAsync($"exams/{exam.ExamId}/dependencies");
        HttpResponseMessage download = await HttpClient.GetAsync($"dependencies/{exam.DependencyId}/download");

        // Assert - named, versioned and sized, so the student knows what is about to arrive.
        list.EnsureSuccessStatusCode();
        DependencyPage page = (await list.Content.ReadFromJsonAsync<DependencyPage>())!;
        page.TotalCount.ShouldBe(1);

        DependencyItem item = page.Items.Single();
        item.Id.ShouldBe(exam.DependencyId);
        item.Name.ShouldBe("GCC");
        item.Version.ShouldBe("14.2.0");
        item.ContentType.ShouldBe("application/gzip");
        item.SizeBytes.ShouldBe(Toolchain.Length);

        // Assert - the bytes come straight from storage, fetched with no API token at all.
        download.EnsureSuccessStatusCode();
        DependencyDownload link = (await download.Content.ReadFromJsonAsync<DependencyDownload>())!;

        using var anonymous = new HttpClient();
        string fetched = await anonymous.GetStringAsync(new Uri(link.DownloadUrl));

        fetched.ShouldBe(Toolchain);
        link.SizeBytes.ShouldBe(Toolchain.Length);
    }

    // Nothing about a draft reaches a student: not the list, and not a download even with the id.
    [Fact]
    public async Task ADraftsDependencies_Should_BeInvisibleToStudents()
    {
        // Arrange
        PreparedExam exam = await PrepareExamAsync(publish: false);
        await AuthenticateAsStudentAsync();

        // Act
        HttpResponseMessage list = await HttpClient.GetAsync($"exams/{exam.ExamId}/dependencies");
        HttpResponseMessage download = await HttpClient.GetAsync($"dependencies/{exam.DependencyId}/download");

        // Assert
        list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_Should_ReturnNotFound_ForAnUnknownDependency()
    {
        // Arrange
        await AuthenticateAsStudentAsync();

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"dependencies/{Guid.NewGuid()}/download");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dependencies_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Arrange
        PreparedExam exam = await PrepareExamAsync(publish: true);
        HttpClient.DefaultRequestHeaders.Authorization = null;

        // Act
        HttpResponseMessage list = await HttpClient.GetAsync($"exams/{exam.ExamId}/dependencies");
        HttpResponseMessage download = await HttpClient.GetAsync($"dependencies/{exam.DependencyId}/download");

        // Assert
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        download.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
