using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.Services.Exams;

internal sealed class ProfessorExams(IApiClient apiClient, ISessionService session) : IProfessorExams
{
    public Task<ApiResult<PagedList<MyExam>>> GetMyExamsAsync(
        int page,
        ExamStatus? status,
        CancellationToken cancellationToken = default) =>
        WithTokenAsync(
            token => apiClient.GetMyExamsAsync(page, PageSize, status, token, cancellationToken),
            cancellationToken);

    public Task<ApiResult<ExamDetails>> GetExamAsync(Guid examId, CancellationToken cancellationToken = default) =>
        WithTokenAsync(token => apiClient.GetExamAsync(examId, token, cancellationToken), cancellationToken);

    public Task<ApiResult<Guid>> CreateExamAsync(
        string title,
        string description,
        string subject,
        CancellationToken cancellationToken = default) =>
        WithTokenAsync(
            token => apiClient.CreateExamAsync(title, description, subject, token, cancellationToken),
            cancellationToken);

    public Task<ApiResult> UpdateExamAsync(
        Guid examId,
        string title,
        string description,
        string subject,
        CancellationToken cancellationToken = default) =>
        WithTokenAsync(
            token => apiClient.UpdateExamAsync(examId, title, description, subject, token, cancellationToken),
            cancellationToken);

    public Task<ApiResult> DeleteExamAsync(Guid examId, CancellationToken cancellationToken = default) =>
        WithTokenAsync(token => apiClient.DeleteExamAsync(examId, token, cancellationToken), cancellationToken);

    public Task<ApiResult> RemoveFileAsync(Guid examId, Guid fileId, CancellationToken cancellationToken = default) =>
        WithTokenAsync(token => apiClient.RemoveExamFileAsync(examId, fileId, token, cancellationToken), cancellationToken);

    public Task<ApiResult> RemoveDependencyAsync(
        Guid examId,
        Guid dependencyId,
        CancellationToken cancellationToken = default) =>
        WithTokenAsync(
            token => apiClient.RemoveExamDependencyAsync(examId, dependencyId, token, cancellationToken),
            cancellationToken);

    public Task<ApiResult> PublishExamAsync(Guid examId, CancellationToken cancellationToken = default) =>
        WithTokenAsync(token => apiClient.PublishExamAsync(examId, token, cancellationToken), cancellationToken);

    public Task<ApiResult<ScheduledSitting>> ScheduleSittingAsync(
        Guid examId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        CancellationToken cancellationToken = default) =>
        WithTokenAsync(
            token => apiClient.CreateExamSessionAsync(examId, startsAt, endsAt, token, cancellationToken),
            cancellationToken);

    public Task<ApiResult<PagedList<MySitting>>> GetMySittingsAsync(
        int page,
        Guid? examId,
        CancellationToken cancellationToken = default) =>
        WithTokenAsync(
            token => apiClient.GetMySittingsAsync(page, PageSize, examId, token, cancellationToken),
            cancellationToken);

    public Task<ApiResult> CancelSittingAsync(Guid sittingId, CancellationToken cancellationToken = default) =>
        WithTokenAsync(token => apiClient.CancelSittingAsync(sittingId, token, cancellationToken), cancellationToken);

    // The token is asked for immediately before the call, so a screen left open for an hour does not
    // send one that expired while it sat there.
    private async Task<ApiResult<T>> WithTokenAsync<T>(
        Func<string, Task<ApiResult<T>>> call,
        CancellationToken cancellationToken)
    {
        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        return token.IsSuccess ? await call(token.Value) : ApiResult.Failure<T>(token.Error);
    }

    private async Task<ApiResult> WithTokenAsync(
        Func<string, Task<ApiResult>> call,
        CancellationToken cancellationToken)
    {
        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        return token.IsSuccess ? await call(token.Value) : ApiResult.Failure(token.Error);
    }

    private const int PageSize = 20;
}
