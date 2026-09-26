namespace SecureExamIDE.Client.Services.Api;

// What a professor's own screens read. Times arrive from the API in UTC and are shown in the
// computer's local time.

// One of the professor's exams, drafts included. CatalogExam is the student's different read of the
// same thing: it shows published exams only, carries the owner's name and never a status.
public sealed record MyExam(
    Guid Id,
    string Title,
    string Subject,
    ExamStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    int FileCount,
    int DependencyCount,
    // Cancelled sittings are counted too: they are still part of the exam's history.
    int SessionCount);

// One exam in full: the description the list leaves out, and the contents the professor manages.
// Readable by its owner alone, draft or published.
public sealed record ExamDetails(
    Guid Id,
    string Title,
    string Description,
    string Subject,
    ExamStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ExamFileInfo> Files,
    IReadOnlyList<ExamDependency> Dependencies);

// A task file attached to an exam. The digest is the server's own measurement of the bytes it
// received, never a number the uploader supplied.
public sealed record ExamFileInfo(Guid Id, string FileName, string ContentType, long SizeBytes, string Sha256);

// Phase one of adding a task file: the server's own measurement of the bytes it received. The
// digest is never a number the client supplies - that is the point of proxying exam material
// through the API instead of straight to storage.
public sealed record UploadedExamFile(string ObjectKey, long SizeBytes, string Sha256);

// Where to PUT a toolchain. Its bytes go straight to storage and never through the API, because a
// compiler is hundreds of megabytes of public archive and proxying it would hold a request open for
// minutes. The key is the server's, so nothing can be written outside this exam's own space.
public sealed record DependencyUploadTarget(string ObjectKey, Uri UploadUrl, DateTimeOffset ExpiresAt);
