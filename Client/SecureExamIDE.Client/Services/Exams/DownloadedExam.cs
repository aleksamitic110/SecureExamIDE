namespace SecureExamIDE.Client.Services.Exams;

// What this computer holds for one exam, written next to the files as exam.json. On exam day the
// student may have no connection at all, so everything the exam screens need to show - which
// sittings were downloaded, when they run, which toolchains are ready - has to be here rather
// than fetched from the API.
public sealed record DownloadedExam(
    Guid ExamId,
    string Title,
    string Subject,
    string Description,
    string ProfessorName,
    IReadOnlyList<DownloadedSitting> Sittings,
    IReadOnlyList<DownloadedDependency> Dependencies,
    DateTimeOffset UpdatedAt);

public sealed record DownloadedSitting(
    Guid SittingId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    long PackageSizeBytes,
    string PackageSha256,
    DateTimeOffset DownloadedAt);

// FileName is relative to the exam's dependency folder and always chosen by the client, never
// taken from the server, so no name the server sends can point outside that folder.
public sealed record DownloadedDependency(
    Guid DependencyId,
    string Name,
    string Version,
    string ContentType,
    long SizeBytes,
    string FileName);
