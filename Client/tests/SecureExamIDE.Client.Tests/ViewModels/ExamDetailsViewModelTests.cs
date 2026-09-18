using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.ExamDay;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class ExamDetailsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly CatalogExam Exam = new(
        Guid.NewGuid(), "Algorithms", "Graphs", "Algorithms and Data Structures", Now, "Milena", "Frtunic", 1, 1, 5000);

    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IExamDownloadService _downloads = Substitute.For<IExamDownloadService>();
    private readonly ILocalExamLibrary _library = Substitute.For<ILocalExamLibrary>();
    private readonly FakeTimeProvider _time = new(Now);

    private static ExamSitting SittingAt(DateTimeOffset startsAt) =>
        new(Guid.NewGuid(), Exam.Id, startsAt, startsAt.AddHours(2), 1000, new string('c', 64));

    private async Task<ExamDetailsViewModel> OpenWithAsync(params ExamSitting[] sittings)
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _catalog.GetSittingsAsync(Exam.Id, Arg.Any<CancellationToken>()).Returns(ApiResult.Success<IReadOnlyList<ExamSitting>>(sittings));
        _catalog.GetDependenciesAsync(Exam.Id, Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success<IReadOnlyList<ExamDependency>>([new ExamDependency(Guid.NewGuid(), "gcc", "13.2", DependencyPlatform.Any, "application/gzip", 4000)]));

        var page = new ExamDetailsViewModel(_session, _navigation, _catalog, _downloads, _library, _time);
        page.Initialize(Exam);
        await page.LoadCommand.ExecuteAsync(null);

        return page;
    }

    // Aleksa asked for this: a downloaded sitting is started where the student already is, rather than
    // through the separate list of exams on this computer.
    [Fact]
    public async Task Enter_Should_OpenTheCodeScreen_ForASittingOnThisComputer()
    {
        // Arrange
        ExamSitting sitting = SittingAt(Now.AddMinutes(-5));
        var downloaded = new DownloadedSitting(sitting.Id, sitting.StartsAt, sitting.EndsAt, 1000, sitting.PackageSha256, Now);
        _library.LoadAsync(Exam.Id, Arg.Any<CancellationToken>()).Returns(new DownloadedExam(
            Exam.Id, Exam.Title, Exam.Subject, Exam.Description, "Milena Frtunic", [downloaded], [], Now));

        using ExamDetailsViewModel page = await OpenWithAsync(sitting);
        SittingItemViewModel item = page.Sittings.ShouldHaveSingleItem();

        // Act
        await page.EnterCommand.ExecuteAsync(item);

        // Assert
        item.CanEnter.ShouldBeTrue();
        item.CanDownload.ShouldBeFalse();
        _navigation.Received(1).NavigateTo(Arg.Any<Action<UnlockSittingViewModel>?>());
    }

    [Fact]
    public async Task Enter_Should_ExplainWhenTheSittingIsNoLongerOnThisComputer()
    {
        // Arrange - listed as downloaded, but the local record has gone since.
        ExamSitting sitting = SittingAt(Now.AddMinutes(-5));
        var downloaded = new DownloadedSitting(sitting.Id, sitting.StartsAt, sitting.EndsAt, 1000, sitting.PackageSha256, Now);
        _library.LoadAsync(Exam.Id, Arg.Any<CancellationToken>()).Returns(
            _ => new DownloadedExam(Exam.Id, Exam.Title, Exam.Subject, Exam.Description, "Milena Frtunic", [downloaded], [], Now),
            _ => null);

        using ExamDetailsViewModel page = await OpenWithAsync(sitting);
        SittingItemViewModel item = page.Sittings.ShouldHaveSingleItem();

        // Act
        await page.EnterCommand.ExecuteAsync(item);

        // Assert
        page.ErrorMessage.ShouldBe("This sitting is no longer on this computer. Download it again.");
        item.IsDownloaded.ShouldBeFalse();
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo<UnlockSittingViewModel>();
    }

    [Fact]
    public async Task Load_Should_DescribeEachSitting_ByWhereItStandsNow()
    {
        // Act
        using ExamDetailsViewModel page = await OpenWithAsync(
            SittingAt(Now.AddDays(-2)),
            SittingAt(Now.AddHours(-1)),
            SittingAt(Now.AddDays(3)));

        // Assert
        page.Sittings.Select(s => s.Status).ShouldBe(["Ended", "In progress", "Upcoming"]);
        page.Sittings.Select(s => s.CanDownload).ShouldBe([false, true, true]);
        page.Sittings[2].When.ShouldBe("Sat 19 Sep 2026, 12:00 - 14:00");
        page.Dependencies.ShouldHaveSingleItem().Name.ShouldBe("gcc 13.2");
        page.Subtitle.ShouldBe("Algorithms and Data Structures · Milena Frtunic");
    }

    [Fact]
    public async Task Load_Should_ShowASittingAsDownloaded_OnlyForTheSamePackage()
    {
        // Arrange
        ExamSitting sitting = SittingAt(Now.AddDays(3));
        ExamSitting resealed = SittingAt(Now.AddDays(4));
        _library.LoadAsync(Exam.Id, Arg.Any<CancellationToken>()).Returns(new DownloadedExam(Exam.Id, "", "", "", "",
        [
            new DownloadedSitting(sitting.Id, sitting.StartsAt, sitting.EndsAt, 1000, sitting.PackageSha256, Now),
            new DownloadedSitting(resealed.Id, resealed.StartsAt, resealed.EndsAt, 1000, new string('d', 64), Now)
        ], [], Now));

        // Act
        using ExamDetailsViewModel page = await OpenWithAsync(sitting, resealed);

        // Assert
        page.Sittings[0].IsDownloaded.ShouldBeTrue();
        page.Sittings[1].IsDownloaded.ShouldBeFalse();
    }

    [Fact]
    public async Task Download_Should_MarkTheSittingDownloaded()
    {
        // Arrange
        using ExamDetailsViewModel page = await OpenWithAsync(SittingAt(Now.AddDays(3)));
        _downloads.DownloadSittingAsync(Exam, page.Sittings[0].Sitting, Arg.Any<IProgress<ExamDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success());

        // Act
        await page.DownloadCommand.ExecuteAsync(page.Sittings[0]);

        // Assert
        page.Sittings[0].IsDownloaded.ShouldBeTrue();
        page.Sittings[0].CanDownload.ShouldBeFalse();
        page.StatusMessage.ShouldNotBeNull();
        page.IsDownloading.ShouldBeFalse();
    }

    [Fact]
    public async Task Download_Should_ExplainACancelledSitting()
    {
        // Arrange
        using ExamDetailsViewModel page = await OpenWithAsync(SittingAt(Now.AddDays(3)));
        _downloads.DownloadSittingAsync(Arg.Any<CatalogExam>(), Arg.Any<ExamSitting>(), Arg.Any<IProgress<ExamDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure(new ApiError(409, ErrorCodes.SittingCancelled, "Cancelled.", [])));

        // Act
        await page.DownloadCommand.ExecuteAsync(page.Sittings[0]);

        // Assert
        page.ErrorMessage.ShouldBe("This sitting has been cancelled by the professor.");
        page.Sittings[0].IsDownloaded.ShouldBeFalse();
    }

    [Fact]
    public async Task StoppingADownload_Should_SayItCanBeResumed()
    {
        // Arrange
        using ExamDetailsViewModel page = await OpenWithAsync(SittingAt(Now.AddDays(3)));
        _downloads.DownloadSittingAsync(Arg.Any<CatalogExam>(), Arg.Any<ExamSitting>(), Arg.Any<IProgress<ExamDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return ApiResult.Success();
            });

        // Act
        Task download = page.DownloadCommand.ExecuteAsync(page.Sittings[0]);
        page.IsDownloading.ShouldBeTrue();
        page.CancelDownloadCommand.Execute(null);
        await download;

        // Assert
        page.StatusMessage.ShouldBe("Download stopped. Starting it again continues where it stopped.");
        page.IsDownloading.ShouldBeFalse();
    }
}
