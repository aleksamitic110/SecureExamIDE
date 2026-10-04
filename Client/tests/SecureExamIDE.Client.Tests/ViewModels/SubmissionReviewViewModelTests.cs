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

    private static OpenedSubmission Solution(bool logComplete = true, int events = 2, int lines = 2) => new(
        [
            new ExamTaskFile("main.c", "int main(void) { return 0; }"u8.ToArray()),
            new ExamTaskFile("util.h", "int helper(void);"u8.ToArray())
        ],
        new ActivityLogContents(
            [.. Enumerable.Range(1, events).Select(sequence =>
                new ActivityEvent(sequence, Now.AddMinutes(sequence), ActivityKind.FileSaved, "main.c"))],
            logComplete,
            lines));

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
