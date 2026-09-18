using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Storage;
using SecureExamIDE.Client.Services.Workspace;

namespace SecureExamIDE.Client.Services.Submission;

// The sealed solution is a zip of the student's files, encrypted with AES-256-GCM under the **hand-in
// key** the unlock derived: HKDF over the package's content key, deliberately **without** this
// computer's machine id, unlike the key the workspace is stored with. The professor holds the
// one-time code, from which the content key follows through the package header, so the professor can
// open exactly this and nothing else about the student's computer. The server stores the bytes and
// can read none of it.
//
// The activity log is written under the same hand-in key and travels exactly as it lies on disk.
internal sealed class SubmissionService(
    IWorkspaceStore workspace,
    IActivityLogStore activityLogs,
    ILocalExamLibrary library,
    IApiClient apiClient,
    ISessionService session,
    TimeProvider timeProvider) : ISubmissionService
{
    public async Task<ApiResult<SealedSubmission>> SealAsync(
        DownloadedExam exam,
        DownloadedSitting sitting,
        byte[] workspaceKey,
        byte[] handInKey,
        CancellationToken cancellationToken = default)
    {
        byte[] archive = [];

        try
        {
            using IWorkspaceFiles files = workspace.Open(exam.ExamId, sitting.SittingId, workspaceKey);
            IReadOnlyList<string> names = files.List();

            archive = Zip(files, names);

            byte[] sealedSolution = Seal(archive, handInKey, sitting.SittingId);

            Directory.CreateDirectory(Path.GetDirectoryName(SolutionPath(exam.ExamId, sitting.SittingId))!);
            AtomicFile.Write(SolutionPath(exam.ExamId, sitting.SittingId), sealedSolution);

            using IActivityLog log = activityLogs.Open(exam.ExamId, sitting.SittingId, handInKey);
            int events = log.Read().Count;

            var submission = new SealedSubmission(
                exam.ExamId,
                sitting.SittingId,
                exam.Title,
                timeProvider.GetUtcNow(),
                sealedSolution.LongLength,
                events,
                HandedInAt: null);

            Write(submission);

            // Nothing editable is left behind: the sealed file is the only copy of the work, which is
            // what makes "the student cannot alter it afterwards" true on this computer as well.
            foreach (string name in names)
            {
                files.Delete(name);
            }

            return ApiResult.Success(submission);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ApiResult.Failure<SealedSubmission>(SubmissionErrors.CannotSeal(exception.Message));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(archive);

            await Task.CompletedTask;
        }
    }

    public SealedSubmission? Find(Guid examId, Guid sittingId)
    {
        string path = RecordPath(examId, sittingId);

        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SealedSubmission>(File.ReadAllBytes(path), JsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<SealedSubmission> FindAll()
    {
        List<SealedSubmission> submissions = [];

        foreach (DownloadedExam exam in LoadAllExams())
        {
            foreach (DownloadedSitting sitting in exam.Sittings)
            {
                if (Find(exam.ExamId, sitting.SittingId) is { } submission)
                {
                    submissions.Add(submission);
                }
            }
        }

        return submissions;
    }

    // Two phases, like every other upload in this project: the bytes first, the row second, and the
    // server measures both digests itself.
    public async Task<ApiResult<SealedSubmission>> HandInAsync(
        SealedSubmission submission,
        CancellationToken cancellationToken = default)
    {
        string solutionPath = SolutionPath(submission.ExamId, submission.SittingId);
        string logPath = activityLogs.PathOf(submission.ExamId, submission.SittingId);

        if (!File.Exists(solutionPath))
        {
            return ApiResult.Failure<SealedSubmission>(SubmissionErrors.Missing);
        }

        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        if (!token.IsSuccess)
        {
            return ApiResult.Failure<SealedSubmission>(token.Error);
        }

        byte[] solution = await File.ReadAllBytesAsync(solutionPath, cancellationToken);
        byte[] activityLog = File.Exists(logPath)
            ? await File.ReadAllBytesAsync(logPath, cancellationToken)
            : Encoding.ASCII.GetBytes(string.Empty);

        ApiResult<UploadedSubmissionContent> uploaded = await apiClient.UploadSubmissionContentAsync(
            submission.SittingId, solution, activityLog, token.Value, cancellationToken);

        if (!uploaded.IsSuccess)
        {
            return ApiResult.Failure<SealedSubmission>(uploaded.Error);
        }

        ApiResult<SubmissionReceipt> receipt = await apiClient.CreateSubmissionAsync(
            submission.SittingId,
            uploaded.Value.Solution.ObjectKey,
            uploaded.Value.ActivityLog.ObjectKey,
            token.Value,
            cancellationToken);

        if (!receipt.IsSuccess)
        {
            return ApiResult.Failure<SealedSubmission>(receipt.Error);
        }

        SealedSubmission handedIn = submission with { HandedInAt = receipt.Value.SubmittedAt };
        Write(handedIn);

        return ApiResult.Success(handedIn);
    }

    private static byte[] Zip(IWorkspaceFiles files, IReadOnlyList<string> names)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string name in names)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(files.Read(name)));
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Seal(byte[] archive, byte[] key, Guid sittingId)
    {
        byte[] sealedBytes = new byte[Magic.Length + NonceSize + TagSize + archive.Length];

        Magic.CopyTo(sealedBytes);
        RandomNumberGenerator.Fill(sealedBytes.AsSpan(Magic.Length, NonceSize));

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(
            sealedBytes.AsSpan(Magic.Length, NonceSize),
            archive,
            sealedBytes.AsSpan(Magic.Length + NonceSize + TagSize),
            sealedBytes.AsSpan(Magic.Length + NonceSize, TagSize),
            Encoding.UTF8.GetBytes($"SecureExamIDE submission v1|{sittingId:N}"));

        return sealedBytes;
    }

    private IReadOnlyList<DownloadedExam> LoadAllExams() =>
        library.LoadAllAsync().GetAwaiter().GetResult();

    private void Write(SealedSubmission submission)
    {
        string path = RecordPath(submission.ExamId, submission.SittingId);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(submission, JsonOptions));
    }

    private string SolutionPath(Guid examId, Guid sittingId) =>
        Path.Combine(library.SittingDirectory(examId, sittingId), "submission.bin");

    private string RecordPath(Guid examId, Guid sittingId) =>
        Path.Combine(library.SittingDirectory(examId, sittingId), "submission.json");

    private static ReadOnlySpan<byte> Magic => "SEIS1"u8;

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
