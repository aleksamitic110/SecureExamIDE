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
    string ContentType,
    long SizeBytes);

public sealed record DependencyDownload(
    Guid DependencyId,
    string Name,
    string Version,
    string ContentType,
    long SizeBytes,
    Uri DownloadUrl,
    DateTimeOffset ExpiresAt);
