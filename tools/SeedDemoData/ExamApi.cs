using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SeedDemoData;

// Everything the tool does goes through the public API, exactly as a professor would: there is no
// back door into the database, and a sitting's package can only be sealed by the server.
internal sealed class ExamApi(HttpClient http, Mailbox mailbox, SeedOptions options)
{
    public async Task WaitForApiAsync()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using HttpResponseMessage health = await http.GetAsync("health");

                if (health.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not up yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new InvalidOperationException($"The API at {options.ApiBaseUrl} did not answer. Is the Docker stack running?");
    }

    // Signs the account in, registering and verifying it first when it does not exist yet. Running
    // the tool a second time therefore changes nothing about the accounts.
    public async Task<string> SignInAsync(DemoAccount account)
    {
        string? token = await TryLoginAsync(account);

        if (token is not null)
        {
            return token;
        }

        DateTimeOffset registeredAt = DateTimeOffset.UtcNow;

        using HttpResponseMessage registered = await http.PostAsJsonAsync("users/register", new
        {
            email = account.Email,
            firstName = account.FirstName,
            lastName = account.LastName,
            password = account.Password,
            role = account.Role,
            indexNumber = account.IndexNumber,
            professorRegistrationCode = account.Role == "Professor" ? options.ProfessorRegistrationCode : null,
            deviceName = "seed-tool"
        });

        if (!registered.IsSuccessStatusCode && registered.StatusCode != HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException($"Registering {account.Email} failed: {await Describe(registered)}");
        }

        await VerifyAsync(account, registeredAt);

        return await TryLoginAsync(account)
            ?? throw new InvalidOperationException($"{account.Email} could not be signed in after verification.");
    }

    private async Task<string?> TryLoginAsync(DemoAccount account)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("users/login", new
        {
            email = account.Email,
            password = account.Password,
            deviceName = "seed-tool"
        });

        if (response.IsSuccessStatusCode)
        {
            LoginResponse body = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

            return body.AccessToken;
        }

        // Registered earlier but never verified - the mail is still in Mailpit, or a new one is asked for.
        if (await IsAsync(response, "Users.EmailNotVerified"))
        {
            await VerifyAsync(account, DateTimeOffset.UtcNow.AddMinutes(-15));

            using HttpResponseMessage retry = await http.PostAsJsonAsync("users/login", new
            {
                email = account.Email,
                password = account.Password,
                deviceName = "seed-tool"
            });

            retry.EnsureSuccessStatusCode();

            return (await retry.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        }

        return null;
    }

    private async Task VerifyAsync(DemoAccount account, DateTimeOffset sentAfter)
    {
        string? code = await mailbox.WaitForVerificationCodeAsync(account.Email, sentAfter);

        if (code is null)
        {
            // The mail may have been cleared out of Mailpit; a fresh one costs nothing.
            using HttpResponseMessage resent = await http.PostAsJsonAsync("users/verify-email/resend", new { email = account.Email });
            resent.EnsureSuccessStatusCode();

            code = await mailbox.WaitForVerificationCodeAsync(account.Email, DateTimeOffset.UtcNow.AddSeconds(-5))
                ?? throw new InvalidOperationException(
                    $"No verification code for {account.Email} arrived in Mailpit at {options.MailpitBaseUrl}.");
        }

        using HttpResponseMessage verified = await http.PostAsJsonAsync("users/verify-email", new { email = account.Email, code });

        if (!verified.IsSuccessStatusCode && !await IsAsync(verified, "Users.InvalidVerificationCode"))
        {
            throw new InvalidOperationException($"Verifying {account.Email} failed: {await Describe(verified)}");
        }
    }

    public void Authenticate(string accessToken) =>
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    public async Task<Guid?> FindExamAsync(string title)
    {
        ExamPage page = (await http.GetFromJsonAsync<ExamPage>("exams/mine?pageSize=100"))!;

        return page.Items.FirstOrDefault(exam => exam.Title == title)?.Id;
    }

    public async Task<Guid> CreateExamAsync(string title, string description, string subject)
    {
        using HttpResponseMessage created = await http.PostAsJsonAsync("exams", new { title, description, subject });

        await EnsureAsync(created, $"creating the exam '{title}'");

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    // Exam files are proxied through the API, which is what lets the server compute their digest.
    public async Task AddFileAsync(Guid examId, string fileName, byte[] content, string contentType)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        using HttpResponseMessage uploaded = await http.PostAsync($"exams/{examId}/files/content", form);
        await EnsureAsync(uploaded, $"uploading {fileName}");

        UploadedContent content1 = (await uploaded.Content.ReadFromJsonAsync<UploadedContent>())!;

        using HttpResponseMessage committed = await http.PostAsJsonAsync(
            $"exams/{examId}/files",
            new { objectKey = content1.ObjectKey, fileName });

        await EnsureAsync(committed, $"recording {fileName}");
    }

    // Dependencies go straight to storage through a presigned link, the way a real toolchain would.
    public async Task AddDependencyAsync(Guid examId, string name, string version, string platform, byte[] archive)
    {
        using HttpResponseMessage ticketResponse = await http.PostAsync($"exams/{examId}/dependencies/upload-url", content: null);
        await EnsureAsync(ticketResponse, $"asking for an upload link for {name}");

        UploadTicket ticket = (await ticketResponse.Content.ReadFromJsonAsync<UploadTicket>())!;

        using (var storage = new HttpClient())
        using (var body = new ByteArrayContent(archive))
        {
            body.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

            using HttpResponseMessage stored = await storage.PutAsync(new Uri(ticket.UploadUrl), body);
            await EnsureAsync(stored, $"uploading {name} to storage");
        }

        using HttpResponseMessage committed = await http.PostAsJsonAsync(
            $"exams/{examId}/dependencies",
            new { objectKey = ticket.ObjectKey, name, version, platform });

        await EnsureAsync(committed, $"recording {name} for {platform}");
    }

    public async Task PublishAsync(Guid examId)
    {
        using HttpResponseMessage published = await http.PatchAsync($"exams/{examId}/publish", content: null);

        if (published.StatusCode != HttpStatusCode.NoContent && !await IsAsync(published, "Exams.CannotPublish"))
        {
            throw new InvalidOperationException($"Publishing the exam failed: {await Describe(published)}");
        }
    }

    // The one-time code is returned once and never again, which is why the tool prints it.
    public async Task<SittingResponse> CreateSittingAsync(Guid examId, DateTime startsAt, DateTime endsAt)
    {
        using HttpResponseMessage created = await http.PostAsJsonAsync($"exams/{examId}/sessions", new { startsAt, endsAt });

        await EnsureAsync(created, "creating the sitting");

        return (await created.Content.ReadFromJsonAsync<SittingResponse>())!;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string what)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{what} failed: {await Describe(response)}");
        }
    }

    private static async Task<bool> IsAsync(HttpResponseMessage response, string errorCode)
    {
        try
        {
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            return problem.RootElement.TryGetProperty("title", out JsonElement title) &&
                   title.GetString() == errorCode;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<string> Describe(HttpResponseMessage response) =>
        $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}";

    private sealed record LoginResponse(string AccessToken);

    private sealed record UploadedContent(string ObjectKey, long SizeBytes, string Sha256);

    private sealed record UploadTicket(string ObjectKey, string UploadUrl, DateTime ExpiresAt);

    private sealed record ExamPage(ExamSummary[] Items);

    private sealed record ExamSummary(Guid Id, string Title, string Status);
}

internal sealed record DemoAccount(
    string Email,
    string FirstName,
    string LastName,
    string Password,
    string Role,
    string? IndexNumber);

internal sealed record SittingResponse(
    Guid SessionId,
    string OneTimeCode,
    DateTime StartsAt,
    DateTime EndsAt,
    long PackageSizeBytes,
    string PackageSha256);
