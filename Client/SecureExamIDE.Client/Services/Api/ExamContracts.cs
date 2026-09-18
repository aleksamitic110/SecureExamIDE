namespace SecureExamIDE.Client.Services.Api;

// Times arrive from the API in UTC and are shown in the computer's local time.

public sealed record CatalogExam(
    Guid Id,
    string Title,
    string Description,
    string Subject,
    DateTimeOffset? PublishedAt,
    string ProfessorFirstName,
    string ProfessorLastName,
    int FileCount,
    int DependencyCount,
    long TotalSizeBytes);

// A sitting of an exam. Each one has its own sealed package and its own one-time code.
public sealed record ExamSitting(
    Guid Id,
    Guid ExamId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    long PackageSizeBytes,
    string PackageSha256);

public sealed record SittingPackage(
    Guid SessionId,
    Uri PackageUrl,
    Uri HeaderUrl,
    DateTimeOffset ExpiresAt,
    long PackageSizeBytes,
    string PackageSha256);

public sealed record ExamDependency(
    Guid Id,
    string Name,
    string Version,
    DependencyPlatform Platform,
    string ContentType,
    long SizeBytes);

public sealed record DependencyDownload(
    Guid DependencyId,
    string Name,
    string Version,
    DependencyPlatform Platform,
    string ContentType,
    long SizeBytes,
    Uri DownloadUrl,
    DateTimeOffset ExpiresAt);

// Phase one of handing in: the server's own measurement of the bytes it received.
public sealed record UploadedSubmissionContent(UploadedPart Solution, UploadedPart ActivityLog);

public sealed record UploadedPart(string ObjectKey, long SizeBytes, string Sha256);

// Phase two: the submission the server recorded. One per student per sitting, ever.
public sealed record SubmissionReceipt(
    Guid SubmissionId,
    DateTimeOffset SubmittedAt,
    long SolutionSizeBytes,
    string SolutionSha256,
    long ActivityLogSizeBytes,
    string ActivityLogSha256);
