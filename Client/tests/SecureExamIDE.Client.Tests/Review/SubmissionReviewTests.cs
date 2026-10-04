using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Downloads;
using SecureExamIDE.Client.Services.Review;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.Tests.Unlock;

namespace SecureExamIDE.Client.Tests.Review;

// The claim under test is the one the whole design rests on: **a professor holding only the sitting's
// one-time code can open what a student handed in**, with nothing from the student's computer. So the
// test seals a submission the way the student's client does, and opens it through the review service.
public sealed class SubmissionReviewTests
{
    private static readonly Guid SittingId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly IFileContentReader _content = Substitute.For<IFileContentReader>();

    private static readonly Uri HeaderUrl = new("http://storage.test/package.hdr");
    private static readonly Uri SolutionUrl = new("http://storage.test/submission.bin");
    private static readonly Uri LogUrl = new("http://storage.test/activity.log");

    private SubmissionReview CreateReview() => new(_api, _session, _content);

    public SubmissionReviewTests()
    {
        _session.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(ApiResult.Success("device-token"));
    }

    private static SubmissionSummary Submission() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Ana", "Anic", "19252", "ana@example.com", "ana-laptop",
        Now, SubmittedAfterSessionEnded: false, 500, new string('a', 64), 200, new string('b', 64));

    // The sitting's package, as the server sealed it; the professor's client fetches its header.
    private byte[] SealSitting()
    {
        (byte[] _, byte[] header, string _) = TestSealer.Seal(new Dictionary<string, string> { ["tasks.txt"] = "1. Sort a list." });

        _api.GetSittingPackageAsync(SittingId, "device-token", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new SittingPackage(
                SittingId, new Uri("http://storage.test/package.bin"), HeaderUrl, Now.AddHours(1), 10, new string('c', 64))));

        _content.ReadAsync(HeaderUrl, Arg.Any<long?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(header));

        return header;
    }

    // What the student's client writes on Finish, and hands in.
    private static (byte[] Solution, byte[] Log) SealWork(byte[] header, IReadOnlyDictionary<string, string> files, int events)
    {
        // The derivation takes the code already normalised, which is what the client does before it.
        SittingKeys.TryDeriveContentKey(
            SittingKeys.ReadHeader(header)!, OneTimeCode.Normalize(TestSealer.Code), out byte[] contentKey)
            .ShouldBeTrue();

        byte[] handInKey = SittingKeys.DeriveHandInKey(contentKey);

        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in files)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        byte[] archive = buffer.ToArray();
        byte[] sealedSolution = new byte[5 + 12 + 16 + archive.Length];

        "SEIS1"u8.CopyTo(sealedSolution);
        RandomNumberGenerator.Fill(sealedSolution.AsSpan(5, 12));

        using (var aes = new AesGcm(handInKey, 16))
        {
            aes.Encrypt(
                sealedSolution.AsSpan(5, 12),
                archive,
                sealedSolution.AsSpan(33),
                sealedSolution.AsSpan(17, 16),
                Encoding.UTF8.GetBytes($"SecureExamIDE submission v1|{SittingId:N}"));
        }

        var log = new StringBuilder();
        string previous = string.Empty;

        for (int sequence = 1; sequence <= events; sequence++)
        {
            var recorded = new ActivityEvent(sequence, Now.AddMinutes(sequence), ActivityKind.FileSaved, $"main.c ({sequence})");
            log.AppendLine(ActivityLogReader.Seal(recorded, previous, SittingId, handInKey));
            previous = ActivityLogReader.DigestOf(recorded);
        }

        return (sealedSolution, Encoding.ASCII.GetBytes(log.ToString()));
    }

    private void SubmissionIs(SubmissionSummary submission, byte[] solution, byte[] log)
    {
        _api.GetSubmissionDownloadAsync(submission.Id, "device-token", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new SubmissionDownload(
                submission.Id, SolutionUrl, LogUrl, Now.AddMinutes(15),
                solution.LongLength, submission.SolutionSha256, log.LongLength, submission.ActivityLogSha256)));

        _content.ReadAsync(SolutionUrl, solution.LongLength, submission.SolutionSha256, Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(solution));

        _content.ReadAsync(LogUrl, log.LongLength, submission.ActivityLogSha256, Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(log));
    }

    [Fact]
    public async Task Open_Should_ReadTheSolutionAndItsLog_FromTheCodeAlone()
    {
        // Arrange
        byte[] header = SealSitting();
        (byte[] solution, byte[] log) = SealWork(
            header,
            new Dictionary<string, string> { ["main.c"] = "int main(void) { return 0; }", ["util.h"] = "int helper(void);" },
            events: 3);

        SubmissionSummary submission = Submission();
        SubmissionIs(submission, solution, log);

        SubmissionReview review = CreateReview();

        // Act - the two steps the screen takes: the code once, then each submission.
        ApiResult<byte[]> handInKey = await review.DeriveHandInKeyAsync(SittingId, TestSealer.Code);
        ApiResult<OpenedSubmission> opened = await review.OpenAsync(SittingId, submission, handInKey.Value);

        // Assert
        handInKey.Value.Length.ShouldBe(32);
        opened.Value.Files.Select(file => file.Name).ShouldBe(["main.c", "util.h"]);
        Encoding.UTF8.GetString(opened.Value.Files[0].Content).ShouldBe("int main(void) { return 0; }");

        opened.Value.ActivityLog.Events.Count.ShouldBe(3);
        opened.Value.ActivityLog.IsComplete.ShouldBeTrue();
        opened.Value.ActivityLog.Events[0].Kind.ShouldBe(ActivityKind.FileSaved);
    }

    // A code typed loosely is the same code: the normalisation is part of the contract.
    [Fact]
    public async Task DeriveHandInKey_Should_AcceptALooselyTypedCode()
    {
        // Arrange
        SealSitting();

        // Act
        ApiResult<byte[]> key = await CreateReview().DeriveHandInKeyAsync(SittingId, "b34k x088 d12w 75y6 mjqx");

        // Assert
        key.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task DeriveHandInKey_Should_RefuseTheWrongCode()
    {
        // Arrange
        SealSitting();

        // Act
        ApiResult<byte[]> key = await CreateReview().DeriveHandInKeyAsync(SittingId, "B34K-X088-D12W-75Y6-0000");

        // Assert
        key.IsSuccess.ShouldBeFalse();
        key.Error.Code.ShouldBe("Review.WrongCode");
    }

    [Fact]
    public async Task DeriveHandInKey_Should_RefuseAnIncompleteCode()
    {
        // Act
        ApiResult<byte[]> key = await CreateReview().DeriveHandInKeyAsync(SittingId, "B34K-X088");

        // Assert
        key.Error!.Code.ShouldBe("Unlock.IncompleteCode");
        await _api.DidNotReceiveWithAnyArgs().GetSittingPackageAsync(Guid.Empty, default!, default);
    }

    // Another sitting's code opens another sitting's packages, and nothing of this one.
    [Fact]
    public async Task Open_Should_RefuseASolutionSealedForAnotherSitting()
    {
        // Arrange
        byte[] header = SealSitting();
        (byte[] solution, byte[] log) = SealWork(header, new Dictionary<string, string> { ["main.c"] = "x" }, events: 1);

        SubmissionSummary submission = Submission();
        SubmissionIs(submission, solution, log);

        SubmissionReview review = CreateReview();
        ApiResult<byte[]> handInKey = await review.DeriveHandInKeyAsync(SittingId, TestSealer.Code);

        // Act - the same bytes, claimed for a different sitting: the sitting id is authenticated.
        ApiResult<OpenedSubmission> opened = await review.OpenAsync(Guid.NewGuid(), submission, handInKey.Value);

        // Assert
        opened.IsSuccess.ShouldBeFalse();
        opened.Error.Code.ShouldBe("Review.CannotOpen");
    }

    // A student who deleted a line out of the middle of their log: the events before it still read,
    // and the professor is told the rest cannot be trusted.
    [Fact]
    public async Task Open_Should_ReportALogWhoseChainWasBroken()
    {
        // Arrange
        byte[] header = SealSitting();
        (byte[] solution, byte[] log) = SealWork(header, new Dictionary<string, string> { ["main.c"] = "x" }, events: 4);

        string[] lines = Encoding.ASCII.GetString(log).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        byte[] gappedLog = Encoding.ASCII.GetBytes(string.Join(Environment.NewLine, lines[0], lines[2], lines[3]));

        SubmissionSummary submission = Submission();
        SubmissionIs(submission, solution, gappedLog);

        SubmissionReview review = CreateReview();
        ApiResult<byte[]> handInKey = await review.DeriveHandInKeyAsync(SittingId, TestSealer.Code);

        // Act
        ApiResult<OpenedSubmission> opened = await review.OpenAsync(SittingId, submission, handInKey.Value);

        // Assert
        opened.Value.Files.ShouldHaveSingleItem();
        opened.Value.ActivityLog.Events.ShouldHaveSingleItem();
        opened.Value.ActivityLog.IsComplete.ShouldBeFalse();
        opened.Value.ActivityLog.LineCount.ShouldBe(3);
    }
}
