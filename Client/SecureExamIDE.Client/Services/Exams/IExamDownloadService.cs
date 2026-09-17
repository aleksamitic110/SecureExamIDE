using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Exams;

public interface IExamDownloadService
{
    // Downloads everything needed to sit the exam offline: the sitting's sealed package and header,
    // and every dependency of the exam. Files already present and intact are not fetched again.
    // The sitting is recorded in the local library only once all of it is on disk.
    Task<ApiResult> DownloadSittingAsync(
        CatalogExam exam,
        ExamSitting sitting,
        IProgress<ExamDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
