using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Exams;

// A professor's own exams, with the access token handled here so screens do not have to. The
// student's side of the same API is IExamCatalog; the two reads are deliberately different, because
// a professor sees drafts and a student never does.
public interface IProfessorExams
{
    Task<ApiResult<PagedList<MyExam>>> GetMyExamsAsync(
        int page,
        ExamStatus? status,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ExamDetails>> GetExamAsync(Guid examId, CancellationToken cancellationToken = default);

    Task<ApiResult<Guid>> CreateExamAsync(
        string title,
        string description,
        string subject,
        CancellationToken cancellationToken = default);

    Task<ApiResult> UpdateExamAsync(
        Guid examId,
        string title,
        string description,
        string subject,
        CancellationToken cancellationToken = default);

    Task<ApiResult> DeleteExamAsync(Guid examId, CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveFileAsync(Guid examId, Guid fileId, CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveDependencyAsync(Guid examId, Guid dependencyId, CancellationToken cancellationToken = default);

    // Puts the exam in the student catalog. There is no going back from it.
    Task<ApiResult> PublishExamAsync(Guid examId, CancellationToken cancellationToken = default);
}
