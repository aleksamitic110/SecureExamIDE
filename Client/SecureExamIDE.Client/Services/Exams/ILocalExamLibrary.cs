namespace SecureExamIDE.Client.Services.Exams;

// The exams downloaded to this computer. Every path is built from ids the client parses, so the
// layout under the data directory is:
//
//   exams/{examId}/exam.json
//   exams/{examId}/sittings/{sittingId}/package.bin      sealed, opened only with the one-time code
//   exams/{examId}/sittings/{sittingId}/package.hdr
//   exams/{examId}/dependencies/{dependencyId}{extension}
public interface ILocalExamLibrary
{
    string PackagePath(Guid examId, Guid sittingId);

    string HeaderPath(Guid examId, Guid sittingId);

    string DependencyFileName(Guid dependencyId, string contentType);

    string DependencyPath(Guid examId, string fileName);

    Task<DownloadedExam?> LoadAsync(Guid examId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadedExam>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(DownloadedExam exam, CancellationToken cancellationToken = default);
}
