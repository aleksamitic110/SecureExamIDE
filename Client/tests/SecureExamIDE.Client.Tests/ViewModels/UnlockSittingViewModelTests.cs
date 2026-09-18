using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.Lockdown;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.Services.Workspace;
using SecureExamIDE.Client.ViewModels.ExamDay;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class UnlockSittingViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 8, 5, 0, TimeSpan.Zero);

    private static readonly DownloadedSitting Sitting =
        new(Guid.NewGuid(), Now.AddMinutes(-5), Now.AddHours(2), 176, new string('e', 64), Now.AddDays(-2));

    private static readonly DownloadedExam Exam =
        new(Guid.NewGuid(), "Algorithms", "Algorithms and Data Structures", "", "Milena Frtunic", [Sitting], [], Now);

    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IPackageUnlocker _unlocker = Substitute.For<IPackageUnlocker>();
    private readonly ILocalExamLibrary _library = Substitute.For<ILocalExamLibrary>();
    private readonly IWorkspaceStore _workspace = Substitute.For<IWorkspaceStore>();
    private readonly FakeTimeProvider _time = new(Now);

    private UnlockSittingViewModel CreatePage(bool testingBuild = false)
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _library.PackagePath(Exam.ExamId, Sitting.SittingId).Returns("/exams/p.bin");
        _library.HeaderPath(Exam.ExamId, Sitting.SittingId).Returns("/exams/p.hdr");

        var page = new UnlockSittingViewModel(
            _navigation,
            _unlocker,
            _library,
            _workspace,
            Options.Create(new LockdownOptions { AllowEmergencyExit = testingBuild }),
            _time);
        page.Initialize(Exam, Sitting);

        return page;
    }

    [Fact]
    public async Task Unlock_Should_StartTheWorkspace_AndForgetTheCode()
    {
        // Arrange
        using var unlocked = new UnlockedExam([new ExamTaskFile("task.txt", [65])], new byte[32], new byte[32]);
        _unlocker.UnlockAsync("/exams/p.bin", "/exams/p.hdr", Sitting.PackageSha256, "B34K-X088-D12W-75Y6-MJQX", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(unlocked));
        UnlockSittingViewModel page = CreatePage();
        page.Code = "B34K-X088-D12W-75Y6-MJQX";

        // Act
        await page.UnlockCommand.ExecuteAsync(null);

        // Assert
        _navigation.Received(1).NavigateTo(Arg.Any<Action<WorkspaceViewModel>?>());
        page.Code.ShouldBeEmpty();
        page.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Unlock_Should_StayAndExplain_WhenTheCodeIsWrong()
    {
        // Arrange
        _unlocker.UnlockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<UnlockedExam>(new ApiError(0, "Unlock.WrongCode", "This code does not unlock this sitting. Check it and try again.", [])));
        UnlockSittingViewModel page = CreatePage();
        page.Code = "B34K-X088-D12W-75Y6-0000";

        // Act
        await page.UnlockCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("This code does not unlock this sitting. Check it and try again.");
        page.Code.ShouldBe("B34K-X088-D12W-75Y6-0000");
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo<WorkspaceViewModel>();
    }

    // Finishing is final: the code cannot reopen the exam to change the work.
    [Fact]
    public async Task Unlock_Should_BeRefused_WhenTheSittingIsFinished()
    {
        // Arrange
        _workspace.IsFinished(Exam.ExamId, Sitting.SittingId).Returns(true);
        UnlockSittingViewModel page = CreatePage();
        page.Code = "B34K-X088-D12W-75Y6-MJQX";

        // Act
        bool canUnlock = page.UnlockCommand.CanExecute(null);
        await page.UnlockCommand.ExecuteAsync(null);

        // Assert
        canUnlock.ShouldBeFalse();
        page.CanEnterCode.ShouldBeFalse();
        page.Status.ShouldBe("You have finished this sitting. It cannot be opened again.");
        await _unlocker.DidNotReceiveWithAnyArgs().UnlockAsync(default!, default!, default!, default!, default);
    }

    // The same switch that provides the escape hatch: testing an exam must not cost a fresh sitting.
    [Fact]
    public async Task Unlock_Should_OpenAFinishedSittingAgain_InATestingBuild()
    {
        // Arrange
        using var unlocked = new UnlockedExam([new ExamTaskFile("task.txt", [65])], new byte[32], new byte[32]);
        _workspace.IsFinished(Exam.ExamId, Sitting.SittingId).Returns(true);
        _unlocker.UnlockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(unlocked));

        UnlockSittingViewModel page = CreatePage(testingBuild: true);
        page.Code = "B34K-X088-D12W-75Y6-MJQX";

        // Act
        await page.UnlockCommand.ExecuteAsync(null);

        // Assert
        page.CanEnterCode.ShouldBeTrue();
        page.Status.ShouldBe("You finished this sitting. Testing build: the code opens it again anyway.");
        _navigation.Received(1).NavigateTo(Arg.Any<Action<WorkspaceViewModel>?>());
    }

    [Fact]
    public void Initialize_Should_DescribeTheSitting()
    {
        // Act
        UnlockSittingViewModel page = CreatePage();

        // Assert
        page.ExamTitle.ShouldBe("Algorithms");
        page.Details.ShouldBe("Algorithms and Data Structures · Milena Frtunic · Fri 18 Sep 2026, 08:00 - 10:05");
        page.Status.ShouldBe("Enter the code the professor gave out for this sitting.");
    }
}
