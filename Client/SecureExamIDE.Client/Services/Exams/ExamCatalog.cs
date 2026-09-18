using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.Services.Exams;

internal sealed class ExamCatalog(IApiClient apiClient, ISessionService session) : IExamCatalog
{
    public async Task<ApiResult<PagedList<CatalogExam>>> GetPublishedExamsAsync(
        int page,
        CancellationToken cancellationToken = default)
    {
        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        return token.IsSuccess
            ? await apiClient.GetPublishedExamsAsync(page, CatalogPageSize, token.Value, cancellationToken)
            : ApiResult.Failure<PagedList<CatalogExam>>(token.Error);
    }

    public Task<ApiResult<IReadOnlyList<ExamSitting>>> GetSittingsAsync(
        Guid examId,
        CancellationToken cancellationToken = default) =>
        GetAllPagesAsync(
            (page, accessToken) => apiClient.GetExamSittingsAsync(examId, page, MaxPageSize, accessToken, cancellationToken),
            cancellationToken);

    public Task<ApiResult<IReadOnlyList<ExamDependency>>> GetDependenciesAsync(
        Guid examId,
        CancellationToken cancellationToken = default) =>
        GetAllPagesAsync(
            (page, accessToken) => apiClient.GetExamDependenciesAsync(
                examId, page, MaxPageSize, ClientPlatform.Current, accessToken, cancellationToken),
            cancellationToken);

    private async Task<ApiResult<IReadOnlyList<T>>> GetAllPagesAsync<T>(
        Func<int, string, Task<ApiResult<PagedList<T>>>> getPage,
        CancellationToken cancellationToken)
    {
        List<T> items = [];

        for (int page = 1; page <= MaxPages; page++)
        {
            // Asked for on every page: a long list must not outlive the token it started with.
            ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

            if (!token.IsSuccess)
            {
                return ApiResult.Failure<IReadOnlyList<T>>(token.Error);
            }

            ApiResult<PagedList<T>> result = await getPage(page, token.Value);

            if (!result.IsSuccess)
            {
                return ApiResult.Failure<IReadOnlyList<T>>(result.Error);
            }

            items.AddRange(result.Value.Items);

            if (!result.Value.HasNextPage)
            {
                break;
            }
        }

        return ApiResult.Success<IReadOnlyList<T>>(items);
    }

    private const int CatalogPageSize = 20;

    // The API's own ceiling on a page.
    private const int MaxPageSize = 100;

    // A guard against a server that always claims another page.
    private const int MaxPages = 50;
}
