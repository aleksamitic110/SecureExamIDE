using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Exams;

// The published exams as the API lists them, with the device token handled here so screens do
// not have to.
public interface IExamCatalog
{
    Task<ApiResult<PagedList<CatalogExam>>> GetPublishedExamsAsync(int page, CancellationToken cancellationToken = default);

    // Every sitting of the exam, all pages gathered: an exam has a handful, never hundreds.
    Task<ApiResult<IReadOnlyList<ExamSitting>>> GetSittingsAsync(Guid examId, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<ExamDependency>>> GetDependenciesAsync(Guid examId, CancellationToken cancellationToken = default);
}
