using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Exams;

namespace SecureExamIDE.Client.Tests.ActivityLog;

public sealed class EncryptedActivityLogTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ExamId = Guid.NewGuid();
    private static readonly Guid SittingId = Guid.NewGuid();

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ActivityLogStore _store;

    public EncryptedActivityLogTests() => _store = new ActivityLogStore(new LocalExamLibrary(_directory), _time);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private IActivityLog Open(byte[]? key = null) => _store.Open(ExamId, SittingId, key ?? _key);

    private string LogPath => _store.PathOf(ExamId, SittingId);

    [Fact]
    public void Write_Should_RecordWhatHappened_InOrder()
    {
        // Arrange
        using IActivityLog log = Open();

        // Act
        log.Write(ActivityKind.ExamOpened, "Algorithms");
        _time.Advance(TimeSpan.FromMinutes(3));
        log.Write(ActivityKind.FileCreated, "main.c");
        log.Write(ActivityKind.RunFinished, "exit code 0");

        // Assert
        IReadOnlyList<ActivityEvent> events = log.Read();
        events.Select(e => e.Kind).ShouldBe([ActivityKind.ExamOpened, ActivityKind.FileCreated, ActivityKind.RunFinished]);
        events.Select(e => e.Sequence).ShouldBe([1, 2, 3]);
        events[1].At.ShouldBe(Now.AddMinutes(3));
        events[1].Detail.ShouldBe("main.c");
    }

    // The log is evidence, so what is on disk must say nothing by itself.
    [Fact]
    public void Write_Should_LeaveNothingReadableOnDisk()
    {
        // Arrange
        using IActivityLog log = Open();

        // Act
        log.Write(ActivityKind.FileCreated, "main.c");

        // Assert
        string onDisk = File.ReadAllText(LogPath);
        onDisk.ShouldNotContain("main.c");
        onDisk.ShouldNotContain("FileCreated");
    }

    [Fact]
    public void Read_Should_ReturnNothing_WithoutTheExamsKey()
    {
        // Arrange
        using (IActivityLog log = Open())
        {
            log.Write(ActivityKind.ExamOpened, "Algorithms");
        }

        // Act
        using IActivityLog other = Open(RandomNumberGenerator.GetBytes(32));

        // Assert
        other.Read().ShouldBeEmpty();
    }

    // A restart in the middle of an exam continues the same log rather than starting a new one.
    [Fact]
    public void Write_Should_ContinueTheLog_AfterTheApplicationIsReopened()
    {
        // Arrange
        using (IActivityLog first = Open())
        {
            first.Write(ActivityKind.ExamOpened, "Algorithms");
            first.Write(ActivityKind.FileCreated, "main.c");
        }

        // Act
        using IActivityLog second = Open();
        second.Write(ActivityKind.ExamFinished);

        // Assert
        second.Read().Select(e => e.Sequence).ShouldBe([1, 2, 3]);
        second.Read()[^1].Kind.ShouldBe(ActivityKind.ExamFinished);
    }

    // Each event carries the digest of the one before it, so a line taken out of the middle shows.
    [Fact]
    public void Read_Should_StopWhereTheChainIsBroken()
    {
        // Arrange
        using (IActivityLog log = Open())
        {
            log.Write(ActivityKind.ExamOpened, "Algorithms");
            log.Write(ActivityKind.OutsideContentBlocked);
            log.Write(ActivityKind.FileSaved, "main.c");
        }

        string[] lines = File.ReadAllLines(LogPath);

        // The student removes the line saying something was blocked.
        File.WriteAllLines(LogPath, [lines[0], lines[2]]);

        // Act
        using IActivityLog reopened = Open();

        // Assert - the first event still reads, and everything after the gap is refused.
        reopened.Read().Select(e => e.Kind).ShouldBe([ActivityKind.ExamOpened]);
    }

    // The sitting's id is part of what each line authenticates, so a line cannot be moved between
    // sittings even by someone holding the key.
    [Fact]
    public void Read_Should_RefuseALineFromAnotherSitting()
    {
        // Arrange
        var otherSittingId = Guid.NewGuid();

        using (IActivityLog other = _store.Open(ExamId, otherSittingId, _key))
        {
            other.Write(ActivityKind.ExamOpened, "Another sitting");
        }

        string lineFromTheOtherSitting = File.ReadAllLines(_store.PathOf(ExamId, otherSittingId))[0];

        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllLines(LogPath, [lineFromTheOtherSitting]);

        // Act
        using IActivityLog log = Open();

        // Assert
        log.Read().ShouldBeEmpty();
    }
}
