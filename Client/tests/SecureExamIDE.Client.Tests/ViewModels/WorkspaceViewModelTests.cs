using Microsoft.Extensions.Time.Testing;
using System.Security.Cryptography;
using System.Threading.Channels;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Exams;
using Microsoft.Extensions.Options;
using SecureExamIDE.Client.Services.Lockdown;
using SecureExamIDE.Client.Services.Pdf;
using SecureExamIDE.Client.Services.Run;
using SecureExamIDE.Client.Services.Storage;
using SecureExamIDE.Client.Services.Submission;
using SecureExamIDE.Client.Services.Toolchains;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.Services.Workspace;
using SecureExamIDE.Client.ViewModels.ExamDay;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class WorkspaceViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 8, 5, 0, TimeSpan.Zero);

    private static readonly DownloadedSitting Sitting =
        new(Guid.NewGuid(), Now.AddMinutes(-5), Now.AddHours(2), 176, new string('e', 64), Now.AddDays(-2));

    private static readonly DownloadedExam Exam =
        new(Guid.NewGuid(), "Algorithms", "Algorithms and Data Structures", "", "Milena Frtunic", [Sitting], [], Now);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IExamLockdown _lockdown = Substitute.For<IExamLockdown>();
    private readonly IToolchainService _toolchains = Substitute.For<IToolchainService>();
    private readonly IProgramRunner _runner = Substitute.For<IProgramRunner>();
    private readonly IPdfRenderer _pdf = Substitute.For<IPdfRenderer>();
    private readonly IUiPreferences _preferences = Substitute.For<IUiPreferences>();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly WorkspaceStore _store;
    private readonly ActivityLogStore _activityLogs;
    private readonly ISubmissionService _submissions = Substitute.For<ISubmissionService>();
    private readonly List<WorkspaceViewModel> _pages = [];

    // The key an unlock would have derived from the one-time code.
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public WorkspaceViewModelTests()
    {
        _submissions.SealAsync(Arg.Any<DownloadedExam>(), Arg.Any<DownloadedSitting>(), Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(call => ApiResult.Success(new SealedSubmission(
                Exam.ExamId, Sitting.SittingId, Exam.Title, Now, 100, 3, HandedInAt: null)));

        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _preferences.ReadPanels().Returns(new WorkspacePanels());
        _store = new WorkspaceStore(new LocalExamLibrary(_directory));
        _activityLogs = new ActivityLogStore(new LocalExamLibrary(_directory), _time);
    }

    public void Dispose()
    {
        foreach (WorkspaceViewModel page in _pages)
        {
            page.Dispose();
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private WorkspaceViewModel OpenWorkspace(UnlockedExam? unlocked = null)
    {
        var page = new WorkspaceViewModel(
            _navigation,
            _store,
            _activityLogs,
            _submissions,
            _toolchains,
            _runner,
            _pdf,
            _lockdown,
            Options.Create(new LockdownOptions()),
            _preferences,
            _time);
#pragma warning disable CA2000 // The workspace owns the unlocked exam and wipes it when disposed.
        page.Initialize(Exam, Sitting, unlocked ?? new UnlockedExam([new ExamTaskFile("tasks.txt", "1. Sort a list."u8.ToArray())], (byte[])_key.Clone(), (byte[])_key.Clone()));
#pragma warning restore CA2000
        _pages.Add(page);

        return page;
    }

    // What the workspace sees on disk, read the same way it reads it: with the unlock's key.
    private void WriteFile(string name, string text)
    {
        using IWorkspaceFiles files = _store.Open(Exam.ExamId, Sitting.SittingId, _key);
        files.Write(name, text);
    }

    private string ReadFile(string name)
    {
        using IWorkspaceFiles files = _store.Open(Exam.ExamId, Sitting.SittingId, _key);

        return files.Read(name);
    }

    private IReadOnlyList<string> ListFiles()
    {
        using IWorkspaceFiles files = _store.Open(Exam.ExamId, Sitting.SittingId, _key);

        return files.List();
    }

    [Fact]
    public void Initialize_Should_LockTheApplication_AndShowTheTasks()
    {
        // Act
        WorkspaceViewModel page = OpenWorkspace();

        // Assert
        _lockdown.Received(1).Enter();
        page.SelectedTaskText.ShouldBe("1. Sort a list.");
        page.Files.ShouldBeEmpty();
        page.HasNoActiveFile.ShouldBeTrue();
    }

    // Reopening the sitting after a crash brings the work back.
    [Fact]
    public void Initialize_Should_OpenTheFilesAlreadyWritten()
    {
        // Arrange
        WriteFile("main.c", "int main(void) {}");

        // Act
        WorkspaceViewModel page = OpenWorkspace();

        // Assert
        page.Files.Select(file => file.Name).ShouldBe(["main.c"]);
        page.ActiveFile!.Document.Text.ShouldBe("int main(void) {}");
    }

    [Fact]
    public void NewFile_Should_CreateItOnDisk_AndOpenIt()
    {
        // Arrange
        WorkspaceViewModel page = OpenWorkspace();

        // Act
        page.StartNewFileCommand.Execute(null);
        page.FileName = "main.c";
        page.ConfirmFileNameCommand.Execute(null);

        // Assert
        page.IsNamingFile.ShouldBeFalse();
        page.ActiveFile!.Name.ShouldBe("main.c");
        page.OpenFiles.ShouldHaveSingleItem();
        ListFiles().ShouldBe(["main.c"]);
    }

    [Fact]
    public void NewFile_Should_ExplainAndKeepTheBoxOpen_WhenTheNameIsTaken()
    {
        // Arrange
        WriteFile("main.c", "");
        WorkspaceViewModel page = OpenWorkspace();

        // Act
        page.StartNewFileCommand.Execute(null);
        page.FileName = "MAIN.c";
        page.ConfirmFileNameCommand.Execute(null);

        // Assert
        page.IsNamingFile.ShouldBeTrue();
        page.FileNameError.ShouldBe("A file with this name already exists.");
        page.Files.Count.ShouldBe(1);
    }

    [Fact]
    public void Typing_Should_BeSavedShortlyAfterItStops()
    {
        // Arrange
        WriteFile("main.c", "");
        WorkspaceViewModel page = OpenWorkspace();
        WorkspaceFileItem file = page.ActiveFile!;

        // Act
        file.Document.Insert(0, "int main(void) { return 0; }");
        string beforeTheDelay = ReadFile("main.c");
        bool dirtyWhileTyping = file.IsDirty;

        _time.Advance(TimeSpan.FromSeconds(2));

        // Assert
        beforeTheDelay.ShouldBeEmpty();
        dirtyWhileTyping.ShouldBeTrue();
        ReadFile("main.c").ShouldBe("int main(void) { return 0; }");
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Rename_Should_CarryUnsavedTypingToTheNewName()
    {
        // Arrange
        WriteFile("draft.c", "");
        WorkspaceViewModel page = OpenWorkspace();
        WorkspaceFileItem file = page.ActiveFile!;
        file.Document.Insert(0, "typed just now");

        // Act
        page.StartRenameCommand.Execute(file);
        page.FileName = "main.c";
        page.ConfirmFileNameCommand.Execute(null);

        // Assert
        file.Name.ShouldBe("main.c");
        ListFiles().ShouldBe(["main.c"]);
        ReadFile("main.c").ShouldBe("typed just now");
    }

    [Fact]
    public void Delete_Should_RemoveTheFileAndItsTab_OnlyAfterConfirming()
    {
        // Arrange
        WriteFile("main.c", "x");
        WorkspaceViewModel page = OpenWorkspace();
        WorkspaceFileItem file = page.ActiveFile!;

        // Act
        page.AskToDeleteCommand.Execute(file);
        int filesWhileAsking = page.Files.Count;
        page.ConfirmDeleteCommand.Execute(null);

        // Assert
        filesWhileAsking.ShouldBe(1);
        page.Files.ShouldBeEmpty();
        page.OpenFiles.ShouldBeEmpty();
        page.ActiveFile.ShouldBeNull();
        ListFiles().ShouldBeEmpty();
    }

    private void ToolchainIsReady() =>
        _toolchains.PrepareAsync(Arg.Any<DownloadedExam>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success<IReadOnlyList<Toolchain>>(
                [new Toolchain(ToolchainKind.Gcc, "GCC", "14.2.0", "/tools/gcc", "/tools/gcc/bin/gcc", "/tools/gcc/bin/g++")]));

    // Runs the callback the workspace passed in, the way the real runner reports output.
    private void RunnerBehaves(Func<RunRequest, Action<RunOutputLine>, ChannelReader<string>, CancellationToken, Task<RunResult>> behaviour) =>
        _runner.RunAsync(Arg.Any<RunRequest>(), Arg.Any<Action<RunOutputLine>>(), Arg.Any<ChannelReader<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => behaviour(
                call.Arg<RunRequest>(),
                call.Arg<Action<RunOutputLine>>(),
                call.Arg<ChannelReader<string>>(),
                call.Arg<CancellationToken>()));

    // What is on screen is what gets compiled, saved first so a crash cannot lose it.
    [Fact]
    public async Task Run_Should_CompileTheFilesAsTheyAreOnScreen()
    {
        // Arrange
        WriteFile("main.c", "");
        ToolchainIsReady();
        RunRequest? compiled = null;
        RunnerBehaves((request, output, _, _) =>
        {
            compiled = request;
            output(new RunOutputLine("Hello\n", RunOutputKind.Output));

            return Task.FromResult(new RunResult(Compiled: true, ExitCode: 0, TimeSpan.FromSeconds(1)));
        });

        WorkspaceViewModel page = OpenWorkspace();
        page.ActiveFile!.Document.Insert(0, "int main(void) { return 0; }");

        // Act
        await page.RunCommand.ExecuteAsync(null);

        // Assert
        compiled!.Sources.ShouldHaveSingleItem().Text.ShouldBe("int main(void) { return 0; }");
        ReadFile("main.c").ShouldBe("int main(void) { return 0; }");
        page.ConsoleText.ShouldContain("Hello");
        page.ConsoleText.ShouldContain("exit code 0");
        page.IsRunning.ShouldBeFalse();
    }

    // What the student types reaches the program's standard input.
    [Fact]
    public async Task Typing_Should_ReachTheRunningProgram()
    {
        // Arrange
        WriteFile("main.c", "");
        ToolchainIsReady();
        string? received = null;
        RunnerBehaves(async (_, output, input, cancellationToken) =>
        {
            output(new RunOutputLine("Enter a number: ", RunOutputKind.Output));
            received = await input.ReadAsync(cancellationToken);

            return new RunResult(Compiled: true, ExitCode: 0, TimeSpan.FromSeconds(1));
        });

        WorkspaceViewModel page = OpenWorkspace();

        // Act - the run is under way while the answer is typed.
        Task running = page.RunCommand.ExecuteAsync(null);

        while (!page.IsRunning)
        {
            await Task.Delay(10);
        }

        page.InputText = "7";
        page.SendInputCommand.Execute(null);
        await running;

        // Assert
        received.ShouldBe("7");
        page.InputText.ShouldBeEmpty();
        page.ConsoleText.ShouldContain("Enter a number:");
        page.ConsoleText.ShouldContain("7");
    }

    [Fact]
    public async Task Stop_Should_EndTheRun()
    {
        // Arrange
        WriteFile("main.c", "");
        ToolchainIsReady();
        RunnerBehaves(async (_, output, _, cancellationToken) =>
        {
            output(new RunOutputLine("started", RunOutputKind.Output));
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

            return new RunResult(Compiled: true, ExitCode: null, TimeSpan.Zero);
        });

        WorkspaceViewModel page = OpenWorkspace();

        // Act
        Task running = page.RunCommand.ExecuteAsync(null);

        while (!page.IsRunning)
        {
            await Task.Delay(10);
        }

        page.StopCommand.Execute(null);

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await running);
        page.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task Run_Should_SayWhenTheExamHasNoCompilerForThisComputer()
    {
        // Arrange
        WriteFile("main.c", "");
        _toolchains.PrepareAsync(Arg.Any<DownloadedExam>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success<IReadOnlyList<Toolchain>>([]));

        WorkspaceViewModel page = OpenWorkspace();

        // Act
        await page.RunCommand.ExecuteAsync(null);

        // Assert
        page.ConsoleText.ShouldContain("no compiler for this computer");
        await _runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default!, default);
    }

    // The clock is shown, never enforced: the warnings are there to be noticed while the student reads.
    [Fact]
    public void Countdown_Should_CountDown_AndWarnAsTheSittingEnds()
    {
        // Arrange - the sitting ends two hours from now.
        WorkspaceViewModel page = OpenWorkspace();
        string atTheStart = page.TimeLeft;

        // Act
        _time.Advance(TimeSpan.FromMinutes(105));
        string withFifteenMinutesLeft = page.TimeLeft;
        bool endingSoon = page.IsEndingSoon;

        _time.Advance(TimeSpan.FromMinutes(10));
        _time.Advance(TimeSpan.FromMinutes(5));

        // Assert
        atTheStart.ShouldBe("2:00:00 left");
        withFifteenMinutesLeft.ShouldBe("15:00 left");
        endingSoon.ShouldBeTrue();
        page.ConsoleText.ShouldContain("15 minutes left");
        page.ConsoleText.ShouldContain("5 minutes left");
        page.TimeLeft.ShouldBe("The sitting has ended");
    }

    // Aleksa asked for this: the panels can be put away so the editor or the console has the window,
    // and the choice is remembered for next time.
    [Fact]
    public void Panels_Should_StartAsTheyWereLeft_AndRememberEveryChange()
    {
        // Arrange
        _preferences.ReadPanels().Returns(new WorkspacePanels(Files: true, Tasks: false, Console: true));

        // Act
        WorkspaceViewModel page = OpenWorkspace();
        bool tasksAtTheStart = page.IsTasksPanelShown;

        page.ToggleConsolePanelCommand.Execute(null);

        // Assert
        tasksAtTheStart.ShouldBeFalse();
        page.IsFilesPanelShown.ShouldBeTrue();
        page.IsConsolePanelShown.ShouldBeFalse();
        _preferences.Received().WritePanels(new WorkspacePanels(Files: true, Tasks: false, Console: false));
    }

    // Everything a professor would want to see about how the exam was taken, kept encrypted beside the
    // work and handed in with it.
    [Fact]
    public async Task TheExam_Should_BeRecordedInTheActivityLog()
    {
        // Arrange
        WriteFile("main.c", "");
        WorkspaceViewModel page = OpenWorkspace();

        // Act
        page.ActiveFile!.Document.Insert(0, "int main(void) { return 0; }");
        _time.Advance(TimeSpan.FromSeconds(2));

        page.StartNewFileCommand.Execute(null);
        page.FileName = "util.h";
        page.ConfirmFileNameCommand.Execute(null);

        page.AskToFinishCommand.Execute(null);
        await page.ConfirmFinishCommand.ExecuteAsync(null);

        // Assert
        using IActivityLog log = _activityLogs.Open(Exam.ExamId, Sitting.SittingId, _key);
        IReadOnlyList<ActivityEvent> events = log.Read();

        events[0].Kind.ShouldBe(ActivityKind.ExamOpened);
        events.ShouldContain(e => e.Kind == ActivityKind.FileSaved && e.Detail!.StartsWith("main.c", StringComparison.Ordinal));
        events.ShouldContain(e => e.Kind == ActivityKind.FileCreated && e.Detail == "util.h");
        events[^1].Kind.ShouldBe(ActivityKind.ExamFinished);
    }

    // The only way out of the locked workspace.
    [Fact]
    public async Task Finish_Should_SaveEverything_CloseTheSitting_AndUnlockTheApplication()
    {
        // Arrange
        WriteFile("main.c", "");
        WorkspaceViewModel page = OpenWorkspace();
        page.ActiveFile!.Document.Insert(0, "last line typed");

        // Act
        page.AskToFinishCommand.Execute(null);
        await page.ConfirmFinishCommand.ExecuteAsync(null);

        // Assert
        ReadFile("main.c").ShouldBe("last line typed");
        _store.IsFinished(Exam.ExamId, Sitting.SittingId).ShouldBeTrue();
        _lockdown.Received(1).Exit();
        _navigation.Received(1).NavigateTo(Arg.Any<Action<DownloadedExamsViewModel>?>());
    }

    [Fact]
    public void CancellingFinish_Should_KeepTheExamOpen()
    {
        // Arrange
        WorkspaceViewModel page = OpenWorkspace();

        // Act
        page.AskToFinishCommand.Execute(null);
        page.CancelFinishCommand.Execute(null);

        // Assert
        page.IsOverlayShown.ShouldBeFalse();
        _store.IsFinished(Exam.ExamId, Sitting.SittingId).ShouldBeFalse();
        _lockdown.DidNotReceive().Exit();
    }

    [Fact]
    public void LeavingTheExamWindow_Should_BeCountedOnScreen()
    {
        // Arrange
        WorkspaceViewModel page = OpenWorkspace();

        // Act
        _lockdown.ExamWindowLeft += Raise.Event();
        _lockdown.ExamWindowLeft += Raise.Event();

        // Assert
        page.LeftWindowCount.ShouldBe(2);
        page.LeftWindowNote.ShouldBe("You left the exam window 2 times.");
    }

    [Fact]
    public void Dispose_Should_WipeTheTasks_AndNeverLeaveTheApplicationLocked()
    {
        // Arrange
#pragma warning disable CA2000 // Handed to the workspace, which owns and disposes it.
        var unlocked = new UnlockedExam([new ExamTaskFile("tasks.txt", "secret"u8.ToArray())], RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
#pragma warning restore CA2000
        byte[] content = unlocked.Files[0].Content;
        WorkspaceViewModel page = OpenWorkspace(unlocked);

        // Act
        page.Dispose();

        // Assert
        content.ShouldAllBe(b => b == 0);
        _lockdown.Received().Exit();
    }
}
