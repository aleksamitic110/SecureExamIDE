using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Files;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Professor;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class ExamContentsViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid ExamId = Guid.NewGuid();

    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IProfessorExams _exams = Substitute.For<IProfessorExams>();
    private readonly IExamContentService _content = Substitute.For<IExamContentService>();
    private readonly IFilePicker _picker = Substitute.For<IFilePicker>();
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"sei-{Guid.NewGuid():N}.zip");
    private readonly List<ExamContentsViewModel> _pages = [];

    public ExamContentsViewModelTests() => File.WriteAllBytes(_tempFile, new byte[2048]);

    public void Dispose()
    {
        // The page cancels an upload in flight when it is left, so each one is disposed.
        foreach (ExamContentsViewModel page in _pages)
        {
            page.Dispose();
        }

        File.Delete(_tempFile);
    }

    private ExamContentsViewModel CreatePage()
    {
        var page = new ExamContentsViewModel(_session, _navigation, _exams, _content, _picker);
        page.Initialize(ExamId);
        _pages.Add(page);
        return page;
    }

    private static ExamFileInfo File1 => new(Guid.NewGuid(), "tasks.pdf", "application/pdf", 4096, new string('a', 64));

    private static ExamDependency Gcc => new(
        Guid.NewGuid(), "GCC (MinGW-w64)", "14.2.0", DependencyPlatform.WindowsX64, "application/zip", 2098368);

    private void Returns(ExamStatus status, IReadOnlyList<ExamFileInfo> files, IReadOnlyList<ExamDependency> deps) =>
        _exams.GetExamAsync(ExamId, Arg.Any<CancellationToken>()).Returns(ApiResult.Success(new ExamDetails(
            ExamId, "Algorithms", "Graphs", "Algorithms and Data Structures", status, Now.AddDays(-2),
            status == ExamStatus.Draft ? null : Now.AddDays(-1), files, deps)));

    [Fact]
    public async Task Load_Should_ShowTheTaskFilesAndToolchains()
    {
        // Arrange
        Returns(ExamStatus.Draft, [File1], [Gcc]);
        ExamContentsViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Title.ShouldBe("Algorithms");
        page.Files.ShouldHaveSingleItem().FileName.ShouldBe("tasks.pdf");
        page.Dependencies.ShouldHaveSingleItem().Name.ShouldBe("GCC (MinGW-w64) 14.2.0");
        page.Dependencies[0].Platform.ShouldBe("Windows (64-bit)");
        page.IsDraft.ShouldBeTrue();
    }

    // Nothing about a published exam can be changed: students may already have downloaded it.
    [Fact]
    public async Task Load_Should_OfferNoChanges_WhenTheExamIsPublished()
    {
        // Arrange
        Returns(ExamStatus.Published, [File1], []);
        ExamContentsViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.IsDraft.ShouldBeFalse();
        page.AddTaskFileCommand.CanExecute(null).ShouldBeFalse();
        page.BeginAddToolchainCommand.CanExecute(null).ShouldBeFalse();
        page.AskToPublishCommand.CanExecute(null).ShouldBeFalse();
    }

    // The API refuses to publish an exam with nothing in it, so the button says so first.
    [Fact]
    public async Task Publish_Should_BeRefused_WhileTheExamHasNoTaskFiles()
    {
        // Arrange
        Returns(ExamStatus.Draft, [], [Gcc]);
        ExamContentsViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.HasNoFiles.ShouldBeTrue();
        page.AskToPublishCommand.CanExecute(null).ShouldBeFalse();
        page.PublishNote.ShouldContain("at least one task file");
    }

    [Fact]
    public async Task Publish_Should_BeOffered_OnceThereIsATaskFile()
    {
        // Arrange
        Returns(ExamStatus.Draft, [File1], []);
        ExamContentsViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.AskToPublishCommand.CanExecute(null).ShouldBeTrue();
    }

    // Publishing cannot be undone, so it is asked about before anything is sent.
    [Fact]
    public async Task Publish_Should_AskFirst_AndPublishOnlyWhenConfirmed()
    {
        // Arrange
        Returns(ExamStatus.Draft, [File1], []);
        _exams.PublishExamAsync(ExamId, Arg.Any<CancellationToken>()).Returns(ApiResult.Success());
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        page.AskToPublishCommand.Execute(null);

        // Assert
        page.IsConfirming.ShouldBeTrue();
        page.ConfirmQuestion!.ShouldContain("Algorithms");
        await _exams.DidNotReceive().PublishExamAsync(ExamId, Arg.Any<CancellationToken>());

        await page.ConfirmCommand.ExecuteAsync(null);

        await _exams.Received().PublishExamAsync(ExamId, Arg.Any<CancellationToken>());
        page.IsConfirming.ShouldBeFalse();
        page.StatusMessage.ShouldBe("The exam was published.");
    }

    [Fact]
    public async Task Publish_Should_DoNothing_WhenTheQuestionIsDeclined()
    {
        // Arrange
        Returns(ExamStatus.Draft, [File1], []);
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        page.AskToPublishCommand.Execute(null);

        // Act
        page.CancelConfirmCommand.Execute(null);
        await page.ConfirmCommand.ExecuteAsync(null);

        // Assert
        page.IsConfirming.ShouldBeFalse();
        await _exams.DidNotReceive().PublishExamAsync(ExamId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddTaskFile_Should_UploadWhatWasChosen_AndReadTheExamBack()
    {
        // Arrange
        Returns(ExamStatus.Draft, [], []);
        _picker.PickFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PickedFile("tasks.pdf", _tempFile, 2048));
        _content.AddTaskFileAsync(ExamId, _tempFile, "tasks.pdf", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success());
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        await page.AddTaskFileCommand.ExecuteAsync(null);

        // Assert
        await _content.Received().AddTaskFileAsync(ExamId, _tempFile, "tasks.pdf", Arg.Any<CancellationToken>());
        page.StatusMessage!.ShouldContain("tasks.pdf");
        // Once to load, once after the upload.
        await _exams.Received(2).GetExamAsync(ExamId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddTaskFile_Should_DoNothing_WhenTheDialogIsClosedWithoutChoosing()
    {
        // Arrange
        Returns(ExamStatus.Draft, [], []);
        _picker.PickFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((PickedFile?)null);
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        await page.AddTaskFileCommand.ExecuteAsync(null);

        // Assert
        await _content.DidNotReceive().AddTaskFileAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        page.StatusMessage.ShouldBeNull();
    }

    [Fact]
    public async Task AddTaskFile_Should_ShowTheServersMessage_WhenTheUploadIsRefused()
    {
        // Arrange
        Returns(ExamStatus.Draft, [], []);
        _picker.PickFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PickedFile("tasks.pdf", _tempFile, 2048));
        _content.AddTaskFileAsync(ExamId, _tempFile, "tasks.pdf", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure(new ApiError(413, "Http.413", "The file is too large.", [])));
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        await page.AddTaskFileCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("The file is too large.");
        page.StatusMessage.ShouldBeNull();
    }

    // A toolchain needs a name, a version and a chosen archive before it can be uploaded.
    [Fact]
    public async Task AddToolchain_Should_BeRefused_UntilTheFormIsComplete()
    {
        // Arrange
        Returns(ExamStatus.Draft, [File1], []);
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        page.BeginAddToolchainCommand.Execute(null);

        // Act & Assert
        page.IsAddingToolchain.ShouldBeTrue();
        page.AddToolchainCommand.CanExecute(null).ShouldBeFalse();

        page.ToolchainName = "GCC";
        page.ToolchainVersion = "14.2.0";
        page.AddToolchainCommand.CanExecute(null).ShouldBeFalse("no archive has been chosen yet");

        _picker.PickFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PickedFile("gcc.zip", _tempFile, 2048));
        await page.ChooseToolchainFileCommand.ExecuteAsync(null);

        page.ToolchainFileLabel!.ShouldContain("gcc.zip");
        page.AddToolchainCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task AddToolchain_Should_UploadWithTheChosenPlatform()
    {
        // Arrange
        Returns(ExamStatus.Draft, [File1], []);
        _picker.PickFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PickedFile("gcc.tar.gz", _tempFile, 2048));
        _content.AddToolchainAsync(
                ExamId, _tempFile, "GCC", "14.2.0", DependencyPlatform.LinuxX64,
                Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success());
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        page.BeginAddToolchainCommand.Execute(null);
        page.ToolchainName = "  GCC  ";
        page.ToolchainVersion = " 14.2.0 ";
        page.SelectedPlatform = PlatformOption.Options.Single(o => o.Value == DependencyPlatform.LinuxX64);
        await page.ChooseToolchainFileCommand.ExecuteAsync(null);

        // Act
        await page.AddToolchainCommand.ExecuteAsync(null);

        // Assert
        await _content.Received().AddToolchainAsync(
            ExamId, _tempFile, "GCC", "14.2.0", DependencyPlatform.LinuxX64,
            Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>());
        page.IsAddingToolchain.ShouldBeFalse();
        page.StatusMessage!.ShouldContain("GCC 14.2.0");
    }

    [Fact]
    public async Task RemovingAFile_Should_AskFirst_AndRemoveOnlyWhenConfirmed()
    {
        // Arrange
        Returns(ExamStatus.Draft, [File1], []);
        _exams.RemoveFileAsync(ExamId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success());
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        page.AskToRemoveFileCommand.Execute(page.Files[0]);

        // Assert
        page.ConfirmQuestion!.ShouldContain("tasks.pdf");
        await _exams.DidNotReceive().RemoveFileAsync(ExamId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await page.ConfirmCommand.ExecuteAsync(null);

        await _exams.Received().RemoveFileAsync(ExamId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        page.StatusMessage!.ShouldContain("tasks.pdf");
    }

    [Fact]
    public async Task RemovingAToolchain_Should_AskFirst_AndRemoveOnlyWhenConfirmed()
    {
        // Arrange
        Returns(ExamStatus.Draft, [File1], [Gcc]);
        _exams.RemoveDependencyAsync(ExamId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success());
        ExamContentsViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        page.AskToRemoveDependencyCommand.Execute(page.Dependencies[0]);
        await page.ConfirmCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().RemoveDependencyAsync(ExamId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        page.StatusMessage!.ShouldContain("GCC (MinGW-w64) 14.2.0");
    }
}
