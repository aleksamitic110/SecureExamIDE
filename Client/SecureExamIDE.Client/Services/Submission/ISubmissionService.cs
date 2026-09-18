using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;

namespace SecureExamIDE.Client.Services.Submission;

// Finishing an exam, in two halves that must not be confused:
//
// **Sealing** happens the moment the student finishes, on their own computer, with no connection: the
// work is encrypted into one file and the editable copies are deleted. From then on the solution
// cannot be changed - there is nothing left to change, and the sealed file is authenticated.
//
// **Handing in** happens whenever the server can be reached, which may be hours later. The server
// takes one submission per student per sitting, so a second attempt changes nothing there either.
public interface ISubmissionService
{
    Task<ApiResult<SealedSubmission>> SealAsync(
        DownloadedExam exam,
        DownloadedSitting sitting,
        byte[] workspaceKey,
        byte[] handInKey,
        CancellationToken cancellationToken = default);

    SealedSubmission? Find(Guid examId, Guid sittingId);

    IReadOnlyList<SealedSubmission> FindAll();

    Task<ApiResult<SealedSubmission>> HandInAsync(SealedSubmission submission, CancellationToken cancellationToken = default);
}
