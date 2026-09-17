using Microsoft.Extensions.Time.Testing;
using System.Security.Cryptography;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Lockdown;
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
    private readonly FakeTimeProvider _time = new(Now);
    private readonly WorkspaceStore _store;
    private readonly List<WorkspaceViewModel> _pages = [];

    // The key an unlock would have derived from the one-time code.
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public WorkspaceViewModelTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _store = new WorkspaceStore(new LocalExamLibrary(_directory));
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
        var page = new WorkspaceViewModel(_navigation, _store, _lockdown, _time);
#pragma warning disable CA2000 // The workspace owns the unlocked exam and wipes it when disposed.
        page.Initialize(Exam, Sitting, unlocked ?? new UnlockedExam([new ExamTaskFile("tasks.txt", "1. Sort a list."u8.ToArray())], (byte[])_key.Clone()));
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

    // The only way out of the locked workspace.
    [Fact]
    public void Finish_Should_SaveEverything_CloseTheSitting_AndUnlockTheApplication()
    {
        // Arrange
        WriteFile("main.c", "");
        WorkspaceViewModel page = OpenWorkspace();
        page.ActiveFile!.Document.Insert(0, "last line typed");

        // Act
        page.AskToFinishCommand.Execute(null);
        page.ConfirmFinishCommand.Execute(null);

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
        var unlocked = new UnlockedExam([new ExamTaskFile("tasks.txt", "secret"u8.ToArray())], RandomNumberGenerator.GetBytes(32));
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
