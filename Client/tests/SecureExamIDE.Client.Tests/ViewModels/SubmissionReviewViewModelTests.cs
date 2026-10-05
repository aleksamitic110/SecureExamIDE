using System.Text;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Files;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Review;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.ViewModels.Professor;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class SubmissionReviewViewModelTests : IDisposable
{
    private static readonly Guid SittingId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IFilePicker _picker = Substitute.For<IFilePicker>();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static SubmissionItem Handed() => new(new SubmissionSummary(
        Guid.NewGuid(), Guid.NewGuid(), "Ana", "Anic", "19252", "ana@example.com", "ana-laptop",
        Now, SubmittedAfterSessionEnded: false, 500, new string('a', 64), 200, new string('b', 64)));

    private SubmissionReviewViewModel Open(OpenedSubmission opened)
    {
        var page = new SubmissionReviewViewModel(_session, _navigation, _picker);
        page.Initialize(SittingId, "Algorithms", Handed(), opened);

        return page;
    }

    private static OpenedSubmission Solution(
        bool logComplete = true,
        int events = 2,
        int lines = 2,
        ActivityKind kind = ActivityKind.FileSaved) => new(
        [
            new ExamTaskFile("main.c", "int main(void) { return 0; }"u8.ToArray()),
            new ExamTaskFile("util.h", "int helper(void);"u8.ToArray())
        ],
        new ActivityLogContents(
            [.. Enumerable.Range(1, events).Select(sequence =>
                new ActivityEvent(sequence, Now.AddMinutes(sequence), kind, "main.c"))],
            logComplete,
            lines));

    // A log of ordinary work with the two kinds that suggest cheating mixed into it.
    private static OpenedSubmission SolutionWithSuspiciousEvents() => new(
        [new ExamTaskFile("main.c", "int main(void) { return 0; }"u8.ToArray())],
        new ActivityLogContents(
            [
                new ActivityEvent(1, Now, ActivityKind.ExamOpened, "Algorithms"),
                new ActivityEvent(2, Now.AddMinutes(1), ActivityKind.OutsideContentBlocked, null),
                new ActivityEvent(3, Now.AddMinutes(2), ActivityKind.FileSaved, "main.c"),
                new ActivityEvent(4, Now.AddMinutes(3), ActivityKind.ExamWindowLeft, null),
                new ActivityEvent(5, Now.AddMinutes(4), ActivityKind.ExamWindowLeft, null)
            ],
            IsComplete: true,
            5));

    [Fact]
    public void Initialize_Should_ShowTheFilesAndTheLog()
    {
        // Act
        using SubmissionReviewViewModel page = Open(Solution());

        // Assert
        page.Student.ShouldBe("Ana Anic (19252)");
        page.Files.Select(file => file.Name).ShouldBe(["main.c", "util.h"]);
        page.SelectedFile!.Document.Text.ShouldBe("int main(void) { return 0; }");
        page.ActivityEvents.Count.ShouldBe(2);
        page.EvidenceNote.ShouldContain("matched the digests recorded on arrival");
        page.EvidenceNote.ShouldContain("chain unbroken");
        page.IsActivityLogComplete.ShouldBeTrue();
    }

    // A log a student took a line out of: what is left still reads, and the professor is told.
    // The point of the colour: a professor marking a class sees which logs are worth reading without
    // reading every line of each.
    [Fact]
    public void Initialize_Should_MarkTheEventsThatSuggestCheating()
    {
        // Act
        using SubmissionReviewViewModel page = Open(SolutionWithSuspiciousEvents());

        // Assert
        page.HasSuspiciousEvents.ShouldBeTrue();
        page.ActivityEvents.Where(recorded => recorded.IsSuspicious)
            .Select(recorded => recorded.Sequence)
            .ShouldBe([2, 4, 5]);

        // Ordinary work is never marked, or the colour would mean nothing.
        page.ActivityEvents.Where(recorded => !recorded.IsSuspicious)
            .Select(recorded => recorded.Sequence)
            .ShouldBe([1, 3]);
    }

    [Fact]
    public void Initialize_Should_CountBothKindsOfSuspiciousEvent()
    {
        // Act
        using SubmissionReviewViewModel page = Open(SolutionWithSuspiciousEvents());

        // Assert
        page.SuspiciousSummary.ShouldContain("1 blocked paste");
        page.SuspiciousSummary.ShouldContain("2 window leaves");
        page.SuspiciousSummary.ShouldContain("0 reopenings");
    }

    // The two suspicious kinds carry no detail of their own, so the screen has to give them words.
    [Fact]
    public void Initialize_Should_DescribeTheSuspiciousKindsInWords()
    {
        // Act
        using SubmissionReviewViewModel page = Open(SolutionWithSuspiciousEvents());

        // Assert
        page.ActivityEvents[1].Description.ShouldBe("Paste from outside blocked");
        page.ActivityEvents[3].Description.ShouldBe("Left the exam window");
        page.ActivityEvents[2].Description.ShouldBe("FileSaved");
    }

    [Fact]
    public void Initialize_Should_SayNothing_WhenTheLogHoldsOnlyOrdinaryWork()
    {
        // Act
        using SubmissionReviewViewModel page = Open(Solution());

        // Assert
        page.HasSuspiciousEvents.ShouldBeFalse();
        page.SuspiciousSummary.ShouldBeEmpty();
    }

    // The exported log is the half of the evidence that leaves the application, so it carries the same
    // marking - and keeps the kind as the log recorded it, for comparing one student against another.
    [Fact]
    public async Task Export_Should_MarkTheSuspiciousRows_AndKeepTheRecordedKind()
    {
        // Arrange
        _picker.PickFolderAsync(Arg.Any<string>()).Returns(_directory);
        using SubmissionReviewViewModel page = Open(SolutionWithSuspiciousEvents());

        // Act
        await page.ExportCommand.ExecuteAsync(null);

        // Assert
        string log = await File.ReadAllTextAsync(
            Directory.EnumerateFiles(_directory, "activity-log.txt", SearchOption.AllDirectories).Single());

        log.ShouldContain("! ");
        log.ShouldContain("OutsideContentBlocked");
        log.ShouldContain("ExamWindowLeft");
        log.ShouldContain("1 blocked paste");

        // Ordinary rows are not marked.
        log.Split(Environment.NewLine).First(line => line.Contains("FileSaved", StringComparison.Ordinal))
            .ShouldStartWith(" ");
    }

    [Fact]
    public void Initialize_Should_WarnWhenTheLogsChainIsBroken()
    {
        // Act
        using SubmissionReviewViewModel page = Open(Solution(logComplete: false, events: 1, lines: 4));

        // Assert
        page.IsActivityLogComplete.ShouldBeFalse();
        page.EvidenceNote.ShouldContain("chain broken");
        page.ActivityLogWarning.ShouldContain("event 1 of 4 lines");
        page.ActivityLogWarning.ShouldContain("removed, reordered or altered");
    }

    // Exporting is deliberate, and the only way a plain copy reaches the professor's computer.
    [Fact]
    public async Task Export_Should_WriteTheFilesAndTheLogToTheChosenFolder()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        _picker.PickFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_directory);

        using SubmissionReviewViewModel page = Open(Solution());

        // Act
        await page.ExportCommand.ExecuteAsync(null);

        // Assert
        string target = Path.Combine(_directory, "Ana Anic 19252");
        Directory.EnumerateFiles(target).Select(Path.GetFileName).ShouldBe(
            ["main.c", "util.h", "activity-log.txt"], ignoreOrder: true);

        (await File.ReadAllTextAsync(Path.Combine(target, "main.c"), CancellationToken.None))
            .ShouldBe("int main(void) { return 0; }");

        string log = await File.ReadAllTextAsync(Path.Combine(target, "activity-log.txt"), CancellationToken.None);
        log.ShouldContain("Ana Anic (19252) - Algorithms");
        log.ShouldContain("FileSaved");

        page.StatusMessage!.ShouldContain(target);
    }

    [Fact]
    public async Task Export_Should_DoNothing_WhenNoFolderWasChosen()
    {
        // Arrange
        _picker.PickFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        using SubmissionReviewViewModel page = Open(Solution());

        // Act
        await page.ExportCommand.ExecuteAsync(null);

        // Assert
        page.StatusMessage.ShouldBeNull();
        page.ErrorMessage.ShouldBeNull();
    }

    // The student's work does not stay in memory once the professor moves on.
    [Fact]
    public void Dispose_Should_WipeTheOpenedFiles()
    {
        // Arrange
        OpenedSubmission opened = Solution();
        byte[] content = opened.Files[0].Content;
        SubmissionReviewViewModel page = Open(opened);

        // Act
        page.Dispose();

        // Assert
        content.ShouldAllBe(b => b == 0);
    }

    [Fact]
    public void Back_Should_ReturnToTheSubmissionsOfThisSitting()
    {
        // Arrange
        using SubmissionReviewViewModel page = Open(Solution());

        // Act
        page.BackCommand.Execute(null);

        // Assert
        _navigation.Received(1).NavigateTo(Arg.Any<Action<SubmissionsViewModel>?>());
    }
}
