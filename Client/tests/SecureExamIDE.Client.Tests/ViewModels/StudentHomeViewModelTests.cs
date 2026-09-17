using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class StudentHomeViewModelTests
{
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly ILocalExamLibrary _library = Substitute.For<ILocalExamLibrary>();

    private static CatalogExam ExamNamed(string title) => new(
        Guid.NewGuid(), title, "Description", "Subject", DateTimeOffset.UtcNow, "Milena", "Frtunic", 1, 2, 3 * 1024 * 1024);

    private StudentHomeViewModel CreatePage() => new(_session, _navigation, _catalog, _library);

    [Fact]
    public async Task Load_Should_ListTheExams_AndMarkTheDownloadedOnes()
    {
        // Arrange
        CatalogExam downloaded = ExamNamed("Algorithms");
        CatalogExam notDownloaded = ExamNamed("Compilers");
        _catalog.GetPublishedExamsAsync(1, Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new PagedList<CatalogExam>([downloaded, notDownloaded], 1, 20, 21, true, false)));
        _library.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([
            new DownloadedExam(downloaded.Id, "", "", "", "",
                [new DownloadedSitting(Guid.NewGuid(), default, default, 0, "", default)], [], default)
        ]);
        StudentHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Exams.Select(e => e.Title).ShouldBe(["Algorithms", "Compilers"]);
        page.Exams[0].HasDownloadedSitting.ShouldBeTrue();
        page.Exams[1].HasDownloadedSitting.ShouldBeFalse();
        page.Exams[0].Contents.ShouldBe("1 task file · 2 dependencies · 3 MB");
        page.PageLabel.ShouldBe("Page 1 of 2");
        page.NextPageCommand.CanExecute(null).ShouldBeTrue();
        page.PreviousPageCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Load_Should_ShowTheError_WhenTheCatalogCannotBeRead()
    {
        // Arrange
        _catalog.GetPublishedExamsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<PagedList<CatalogExam>>(ApiError.Unreachable("Cannot reach the server.")));
        StudentHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("Cannot reach the server.");
        page.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task Load_Should_SayThereIsNothingYet_WhenTheCatalogIsEmpty()
    {
        // Arrange
        _catalog.GetPublishedExamsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new PagedList<CatalogExam>([], 1, 20, 0, false, false)));
        _library.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        StudentHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.IsEmpty.ShouldBeTrue();
    }
}
