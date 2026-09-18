using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureExamIDE.Client.Services.Api;

// The typed HTTP client for the SecureExamIDE API. Every call returns an ApiResult: a response the
// server rejected becomes an ApiError read from its problem details, and a server that cannot be
// reached becomes an ApiError too, so no screen has to catch HTTP exceptions.
internal sealed class ApiClient(HttpClient httpClient) : IApiClient
{
    public Task<ApiResult<RegisterResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<RegisterResponse>(HttpMethod.Post, "users/register", request, null, cancellationToken);

    public Task<ApiResult> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "users/verify-email", request, null, cancellationToken);

    public Task<ApiResult> ResendVerificationCodeAsync(
        ResendVerificationCodeRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "users/verify-email/resend", request, null, cancellationToken);

    public Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        SendForValueAsync<LoginResponse>(HttpMethod.Post, "users/login", request, null, cancellationToken);

    public Task<ApiResult<DeviceTokenResponse>> IssueDeviceTokenAsync(
        DeviceTokenRequest request,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<DeviceTokenResponse>(HttpMethod.Post, "auth/device-token", request, null, cancellationToken);

    public Task<ApiResult<UserProfile>> GetUserAsync(
        Guid userId,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<UserProfile>(HttpMethod.Get, $"users/{userId}", null, accessToken, cancellationToken);

    public Task<ApiResult> RevokeDeviceAsync(Guid deviceId, string accessToken, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"devices/{deviceId}", null, accessToken, cancellationToken);

    public Task<ApiResult<PagedList<CatalogExam>>> GetPublishedExamsAsync(
        int page,
        int pageSize,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<PagedList<CatalogExam>>(
            HttpMethod.Get, PagedPath("exams", page, pageSize), null, accessToken, cancellationToken);

    public Task<ApiResult<PagedList<ExamSitting>>> GetExamSittingsAsync(
        Guid examId,
        int page,
        int pageSize,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<PagedList<ExamSitting>>(
            HttpMethod.Get, PagedPath($"exams/{examId}/sessions", page, pageSize), null, accessToken, cancellationToken);

    public Task<ApiResult<SittingPackage>> GetSittingPackageAsync(
        Guid sittingId,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<SittingPackage>(HttpMethod.Get, $"sessions/{sittingId}/package", null, accessToken, cancellationToken);

    // The platform filter is what keeps a Windows laptop from downloading the Linux compiler: the
    // API answers with that platform's toolchains plus the ones that run anywhere.
    public Task<ApiResult<PagedList<ExamDependency>>> GetExamDependenciesAsync(
        Guid examId,
        int page,
        int pageSize,
        DependencyPlatform? platform,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<PagedList<ExamDependency>>(
            HttpMethod.Get,
            PagedPath($"exams/{examId}/dependencies", page, pageSize) + (platform is null ? string.Empty : $"&platform={platform}"),
            null,
            accessToken,
            cancellationToken);

    public Task<ApiResult<DependencyDownload>> GetDependencyDownloadAsync(
        Guid dependencyId,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<DependencyDownload>(
            HttpMethod.Get, $"dependencies/{dependencyId}/download", null, accessToken, cancellationToken);

    // The solution and its log go together in one request: the server refuses either on its own, and
    // it measures both digests from the bytes it receives.
    public async Task<ApiResult<UploadedSubmissionContent>> UploadSubmissionContentAsync(
        Guid sittingId,
        byte[] solution,
        byte[] activityLog,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var solutionContent = new ByteArrayContent(solution);
        using var activityLogContent = new ByteArrayContent(activityLog);

        solutionContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        activityLogContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        form.Add(solutionContent, "solution", "solution.bin");
        form.Add(activityLogContent, "activityLog", "activity.log");

        (HttpResponseMessage? response, ApiError? error) = await ExchangeContentAsync(
            HttpMethod.Post, $"sessions/{sittingId}/submissions/content", form, accessToken, cancellationToken);

        if (error is not null)
        {
            return ApiResult.Failure<UploadedSubmissionContent>(error);
        }

        using (response)
        {
            try
            {
                UploadedSubmissionContent? uploaded =
                    await response!.Content.ReadFromJsonAsync<UploadedSubmissionContent>(JsonOptions, cancellationToken);

                return uploaded is null
                    ? ApiResult.Failure<UploadedSubmissionContent>(UnexpectedResponse(response.StatusCode))
                    : ApiResult.Success(uploaded);
            }
            catch (JsonException)
            {
                return ApiResult.Failure<UploadedSubmissionContent>(UnexpectedResponse(response!.StatusCode));
            }
        }
    }

    public Task<ApiResult<SubmissionReceipt>> CreateSubmissionAsync(
        Guid sittingId,
        string solutionObjectKey,
        string activityLogObjectKey,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendForValueAsync<SubmissionReceipt>(
            HttpMethod.Post,
            $"sessions/{sittingId}/submissions",
            new { solutionObjectKey, activityLogObjectKey },
            accessToken,
            cancellationToken);

    private static string PagedPath(string path, int page, int pageSize) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}?page={page}&pageSize={pageSize}");

    private async Task<ApiResult<T>> SendForValueAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        (HttpResponseMessage? response, ApiError? error) = await ExchangeAsync(method, path, body, accessToken, cancellationToken);

        if (response is null)
        {
            return ApiResult.Failure<T>(error!);
        }

        using (response)
        {
            try
            {
                T? value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);

                return value is null
                    ? ApiResult.Failure<T>(UnexpectedResponse(response.StatusCode))
                    : ApiResult.Success(value);
            }
            catch (JsonException)
            {
                return ApiResult.Failure<T>(UnexpectedResponse(response.StatusCode));
            }
        }
    }

    private async Task<ApiResult> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        (HttpResponseMessage? response, ApiError? error) = await ExchangeAsync(method, path, body, accessToken, cancellationToken);

        response?.Dispose();

        return error is null ? ApiResult.Success() : ApiResult.Failure(error);
    }

    // Sends one request. A success hands the response back for the caller to read and dispose;
    // anything else is turned into an ApiError here.
    private async Task<(HttpResponseMessage? Response, ApiError? Error)> ExchangeAsync(
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using HttpContent? content = body is null ? null : JsonContent.Create(body, body.GetType(), options: JsonOptions);

        return await ExchangeContentAsync(method, path, content, accessToken, cancellationToken);
    }

    // The same exchange for a request whose body is not JSON: handing in sends the sealed solution and
    // its log as a form, because that is what the API's two-file endpoint takes.
    private async Task<(HttpResponseMessage? Response, ApiError? Error)> ExchangeContentAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            return (null, ApiError.Unreachable(
                "The server address is not configured. Set Api:BaseUrl in appsettings.Local.json."));
        }

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        request.Content = content;

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return (null, ServerUnreachable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation nobody asked for.
            return (null, ServerUnreachable());
        }

        if (response.IsSuccessStatusCode)
        {
            return (response, null);
        }

        using (response)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }
    }

    private ApiError ServerUnreachable() =>
        ApiError.Unreachable($"Cannot reach the server at {httpClient.BaseAddress}. Check your internet connection.");

    private static async Task<ApiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int statusCode = (int)response.StatusCode;

        try
        {
            ProblemDetailsBody? problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(
                JsonOptions, cancellationToken);

            if (problem?.Title is not null)
            {
                return new ApiError(
                    statusCode,
                    problem.Title,
                    problem.Detail ?? DefaultMessageFor(response.StatusCode),
                    [.. (problem.Errors ?? []).Select(e => e.Description).OfType<string>()]);
            }
        }
        catch (JsonException)
        {
            // Not every failure has a problem-details body: an expired token's 401 from the
            // authentication middleware and a 429 from the rate limiter arrive empty.
        }

        return new ApiError(statusCode, $"Http.{statusCode}", DefaultMessageFor(response.StatusCode), []);
    }

    private static ApiError UnexpectedResponse(HttpStatusCode statusCode) =>
        new((int)statusCode, "Client.UnexpectedResponse", "The server sent a response the application did not understand.", []);

    private static string DefaultMessageFor(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "Your session is no longer valid. Sign in again.",
        HttpStatusCode.Forbidden => "You do not have access to this.",
        HttpStatusCode.NotFound => "It was not found.",
        HttpStatusCode.TooManyRequests => "Too many attempts. Wait a minute and try again.",
        >= HttpStatusCode.InternalServerError => "The server ran into a problem. Try again later.",
        _ => "The request could not be completed."
    };

    private sealed record ProblemDetailsBody(string? Title, string? Detail, IReadOnlyList<ProblemDetailsItem>? Errors);

    private sealed record ProblemDetailsItem(string? Code, string? Description);

    // Web defaults (camelCase) plus enums as text, matching how the API writes JSON.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
