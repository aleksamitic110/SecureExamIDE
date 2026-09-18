using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Submission;
using SecureExamIDE.Client.Services.Workspace;

namespace SecureExamIDE.Client.Tests.Submission;

public sealed class SubmissionServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 11, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    // The two keys the unlock hands out: one bound to this computer, one the professor can derive.
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _handInKey = RandomNumberGenerator.GetBytes(32);
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly LocalExamLibrary _library;
    private readonly WorkspaceStore _workspace;
    private readonly ActivityLogStore _activityLogs;
    private readonly SubmissionService _submissions;

    private static readonly DownloadedSitting Sitting = new(
        Guid.NewGuid(), Now.AddHours(-2), Now.AddHours(1), 1000, new string('a', 64), Now.AddDays(-1));

    private static readonly DownloadedExam Exam = new(
        Guid.NewGuid(), "Algorithms", "Algorithms and Data Structures", "", "Milena Frtunic", [Sitting], [], Now);

    public SubmissionServiceTests()
    {
        _library = new LocalExamLibrary(_directory);
        _workspace = new WorkspaceStore(_library);
        _activityLogs = new ActivityLogStore(_library, _time);
        _submissions = new SubmissionService(_workspace, _activityLogs, _library, _api, _session, _time);

        _session.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(ApiResult.Success("device-token"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<SealedSubmission> SealWorkAsync()
    {
        await _library.SaveAsync(Exam);

        using (IWorkspaceFiles files = _workspace.Open(Exam.ExamId, Sitting.SittingId, _key))
        {
            files.Write("main.c", "int main(void) { return 0; }");
            files.Write("util.h", "int helper(void);");
        }

        using (IActivityLog log = _activityLogs.Open(Exam.ExamId, Sitting.SittingId, _handInKey))
        {
            log.Write(ActivityKind.ExamOpened, "Algorithms");
            log.Write(ActivityKind.FileSaved, "main.c");
        }

        ApiResult<SealedSubmission> result = await _submissions.SealAsync(Exam, Sitting, _key, _handInKey);

        result.IsSuccess.ShouldBeTrue();

        return result.Value;
    }

    private string SolutionPath =>
        Path.Combine(_library.SittingDirectory(Exam.ExamId, Sitting.SittingId), "submission.bin");

    // "On finish, the solution is encrypted and stored locally - the student cannot alter it
    // afterwards": there is nothing editable left to alter.
    [Fact]
    public async Task Seal_Should_EncryptTheWork_AndLeaveNothingEditableBehind()
    {
        // Act
        SealedSubmission submission = await SealWorkAsync();

        // Assert
        submission.ExamTitle.ShouldBe("Algorithms");
        submission.ActivityEventCount.ShouldBe(2);
        submission.IsHandedIn.ShouldBeFalse();

        File.Exists(SolutionPath).ShouldBeTrue();
        Encoding.UTF8.GetString(await File.ReadAllBytesAsync(SolutionPath, CancellationToken.None)).ShouldNotContain("int main");

        using IWorkspaceFiles files = _workspace.Open(Exam.ExamId, Sitting.SittingId, _key);
        files.List().ShouldBeEmpty();
    }

    // The professor opens it with the one-time code, which is where this key comes from; the server
    // stores the bytes and can read none of it.
    [Fact]
    public async Task Seal_Should_ProduceAnArchiveTheProfessorsKeyOpens()
    {
        // Arrange
        await SealWorkAsync();

        byte[] sealedBytes = await File.ReadAllBytesAsync(SolutionPath, CancellationToken.None);
        // Act - opened exactly as a professor's tool would, from the sealed file alone.
        byte[] archive = new byte[sealedBytes.Length - 5 - 12 - 16];

        using var aes = new AesGcm(_handInKey, 16);
        aes.Decrypt(
            sealedBytes.AsSpan(5, 12),
            sealedBytes.AsSpan(5 + 12 + 16),
            sealedBytes.AsSpan(5 + 12, 16),
            archive,
            Encoding.UTF8.GetBytes($"SecureExamIDE submission v1|{Sitting.SittingId:N}"));

        using var buffer = new MemoryStream(archive);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);

        // Assert
        zip.Entries.Select(entry => entry.FullName).ShouldBe(["main.c", "util.h"], ignoreOrder: true);

        using var reader = new StreamReader(await zip.GetEntry("main.c")!.OpenAsync(CancellationToken.None));
        (await reader.ReadToEndAsync(CancellationToken.None)).ShouldBe("int main(void) { return 0; }");
    }

    [Fact]
    public async Task HandIn_Should_UploadTheSolutionWithItsLog_AndRecordWhenItLanded()
    {
        // Arrange
        SealedSubmission submission = await SealWorkAsync();

        _api.UploadSubmissionContentAsync(Sitting.SittingId, Arg.Any<byte[]>(), Arg.Any<byte[]>(), "device-token", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new UploadedSubmissionContent(
                new UploadedPart("solution-key", 100, new string('b', 64)),
                new UploadedPart("log-key", 50, new string('c', 64)))));

        _api.CreateSubmissionAsync(Sitting.SittingId, "solution-key", "log-key", "device-token", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new SubmissionReceipt(
                Guid.NewGuid(), Now.AddMinutes(5), 100, new string('b', 64), 50, new string('c', 64))));

        // Act
        ApiResult<SealedSubmission> result = await _submissions.HandInAsync(submission);

        // Assert
        result.Value.HandedInAt.ShouldBe(Now.AddMinutes(5));
        _submissions.Find(Exam.ExamId, Sitting.SittingId)!.IsHandedIn.ShouldBeTrue();

        // The log went with it, as one unit: the server refuses either half on its own.
        await _api.Received(1).UploadSubmissionContentAsync(
            Sitting.SittingId,
            Arg.Is<byte[]>(solution => solution.Length > 0),
            Arg.Is<byte[]>(log => log.Length > 0),
            "device-token",
            Arg.Any<CancellationToken>());
    }

    // The exam room has no connection; handing in waits, and the sealed work is untouched meanwhile.
    [Fact]
    public async Task HandIn_Should_KeepTheWork_WhenTheServerCannotBeReached()
    {
        // Arrange
        SealedSubmission submission = await SealWorkAsync();

        _session.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<string>(ApiError.Unreachable("No connection.")));

        // Act
        ApiResult<SealedSubmission> result = await _submissions.HandInAsync(submission);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.IsUnreachable.ShouldBeTrue();

        SealedSubmission stored = _submissions.Find(Exam.ExamId, Sitting.SittingId)!;
        stored.IsHandedIn.ShouldBeFalse();
        File.Exists(SolutionPath).ShouldBeTrue();
    }

    [Fact]
    public async Task FindAll_Should_ListWhatIsWaitingToBeHandedIn()
    {
        // Arrange
        await SealWorkAsync();

        // Act
        IReadOnlyList<SealedSubmission> waiting = _submissions.FindAll();

        // Assert
        waiting.ShouldHaveSingleItem().SittingId.ShouldBe(Sitting.SittingId);
    }
}
