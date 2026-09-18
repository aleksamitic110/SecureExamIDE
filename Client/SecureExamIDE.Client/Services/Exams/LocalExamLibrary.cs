using System.Globalization;
using System.Text.Json;
using SecureExamIDE.Client.Services.Storage;

namespace SecureExamIDE.Client.Services.Exams;

internal sealed class LocalExamLibrary(string dataDirectory) : ILocalExamLibrary
{
    private string ExamsDirectory => Path.Combine(dataDirectory, "exams");

    public string PackagePath(Guid examId, Guid sittingId) =>
        Path.Combine(SittingDirectory(examId, sittingId), "package.bin");

    public string HeaderPath(Guid examId, Guid sittingId) =>
        Path.Combine(SittingDirectory(examId, sittingId), "package.hdr");

    // The extension is kept so the workspace can later tell a zip from a tarball without sniffing.
    public string DependencyFileName(Guid dependencyId, string contentType) =>
        string.Create(CultureInfo.InvariantCulture, $"{dependencyId:N}{ExtensionFor(contentType)}");

    public string DependencyPath(Guid examId, string fileName) =>
        Path.Combine(ExamDirectory(examId), "dependencies", Path.GetFileName(fileName));

    public string ToolsDirectory(Guid examId) => Path.Combine(ExamDirectory(examId), "tools");

    public async Task<DownloadedExam?> LoadAsync(Guid examId, CancellationToken cancellationToken = default)
    {
        string path = ManifestPath(examId);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream file = File.OpenRead(path);

            return await JsonSerializer.DeserializeAsync<DownloadedExam>(file, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // An unreadable manifest is treated as nothing downloaded; downloading again rewrites it.
            return null;
        }
    }

    public async Task<IReadOnlyList<DownloadedExam>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ExamsDirectory))
        {
            return [];
        }

        List<DownloadedExam> exams = [];

        foreach (string directory in Directory.EnumerateDirectories(ExamsDirectory))
        {
            if (Guid.TryParse(Path.GetFileName(directory), out Guid examId) &&
                await LoadAsync(examId, cancellationToken) is { } exam)
            {
                exams.Add(exam);
            }
        }

        return exams;
    }

    public async Task SaveAsync(DownloadedExam exam, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ExamDirectory(exam.ExamId));

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(exam, JsonOptions);

        await AtomicFile.WriteAsync(ManifestPath(exam.ExamId), json, cancellationToken);
    }

    private string ExamDirectory(Guid examId) => Path.Combine(ExamsDirectory, examId.ToString("N"));

    public string SittingDirectory(Guid examId, Guid sittingId) =>
        Path.Combine(ExamDirectory(examId), "sittings", sittingId.ToString("N"));

    private string ManifestPath(Guid examId) => Path.Combine(ExamDirectory(examId), "exam.json");

    private static string ExtensionFor(string contentType) => contentType.ToUpperInvariant() switch
    {
        "APPLICATION/ZIP" or "APPLICATION/X-ZIP-COMPRESSED" => ".zip",
        "APPLICATION/GZIP" or "APPLICATION/X-GZIP" => ".tar.gz",
        "APPLICATION/X-TAR" => ".tar",
        "APPLICATION/X-7Z-COMPRESSED" => ".7z",
        "APPLICATION/X-XZ" => ".tar.xz",
        _ => ".bin"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
