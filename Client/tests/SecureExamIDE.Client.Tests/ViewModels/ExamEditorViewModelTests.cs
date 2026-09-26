using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Professor;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class ExamEditorViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IProfessorExams _exams = Substitute.For<IProfessorExams>();

    private ExamEditorViewModel CreatePage(Guid? examId)
    {
        var page = new ExamEditorViewModel(_session, _navigation, _exams);
        page.Initialize(examId);
        return page;
    }

    private static ExamDetails Details(ExamStatus status) => new(
        Guid.NewGuid(), "Algorithms", "Graphs and sorting", "Algorithms and Data Structures",
        status, Now.AddDays(-2), status == ExamStatus.Draft ? null : Now.AddDays(-1), [], []);

    [Fact]
    public async Task NewExam_Should_StartOnAnEmptyForm()
    {
        // Arrange
        ExamEditorViewModel page = CreatePage(null);

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.IsNew.ShouldBeTrue();
        page.Heading.ShouldBe("New exam");
        page.Title.ShouldBeEmpty();
        await _exams.DidNotReceive().GetExamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // The list leaves the description out, so the editor reads the whole exam to fill the form.
    [Fact]
    public async Task EditingADraft_Should_FillTheFormFromTheWholeExam()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _exams.GetExamAsync(examId, Arg.Any<CancellationToken>()).Returns(ApiResult.Success(Details(ExamStatus.Draft)));
        ExamEditorViewModel page = CreatePage(examId);

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.IsNew.ShouldBeFalse();
        page.Heading.ShouldBe("Edit exam");
        page.Title.ShouldBe("Algorithms");
        page.Description.ShouldBe("Graphs and sorting");
        page.Subject.ShouldBe("Algorithms and Data Structures");
        page.IsReadOnly.ShouldBeFalse();
        page.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    // A published exam is fixed: students may already have downloaded it.
    [Fact]
    public async Task EditingAPublishedExam_Should_ShowItButRefuseToSave()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _exams.GetExamAsync(examId, Arg.Any<CancellationToken>()).Returns(ApiResult.Success(Details(ExamStatus.Published)));
        ExamEditorViewModel page = CreatePage(examId);

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Title.ShouldBe("Algorithms");
        page.IsReadOnly.ShouldBeTrue();
        page.ErrorMessage.ShouldBe("This exam is published and can no longer be changed.");
        page.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Save_Should_CreateTheExam_AndGoBackToTheList()
    {
        // Arrange
        _exams.CreateExamAsync("Algorithms", "Graphs", "ADS", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(Guid.NewGuid()));
        ExamEditorViewModel page = CreatePage(null);
        page.Title = "Algorithms";
        page.Description = "Graphs";
        page.Subject = "ADS";

        // Act
        await page.SaveCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().CreateExamAsync("Algorithms", "Graphs", "ADS", Arg.Any<CancellationToken>());
        _navigation.Received().NavigateTo<ProfessorHomeViewModel>();
    }

    [Fact]
    public async Task Save_Should_UpdateTheExam_WhenOneIsBeingEdited()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _exams.GetExamAsync(examId, Arg.Any<CancellationToken>()).Returns(ApiResult.Success(Details(ExamStatus.Draft)));
        _exams.UpdateExamAsync(examId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success());
        ExamEditorViewModel page = CreatePage(examId);
        await page.LoadCommand.ExecuteAsync(null);
        page.Title = "Algorithms - September";

        // Act
        await page.SaveCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().UpdateExamAsync(
            examId, "Algorithms - September", "Graphs and sorting", "Algorithms and Data Structures",
            Arg.Any<CancellationToken>());
        _navigation.Received().NavigateTo<ProfessorHomeViewModel>();
    }

    // The server trims before it measures, so the client sends what the server would keep.
    [Fact]
    public async Task Save_Should_TrimTheFields()
    {
        // Arrange
        _exams.CreateExamAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(Guid.NewGuid()));
        ExamEditorViewModel page = CreatePage(null);
        page.Title = "  Algorithms  ";
        page.Description = " Graphs ";
        page.Subject = " ADS ";

        // Act
        await page.SaveCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().CreateExamAsync("Algorithms", "Graphs", "ADS", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Save_Should_BeRefused_WhenAFieldIsEmptyOrOnlySpaces()
    {
        // Arrange
        ExamEditorViewModel page = CreatePage(null);

        // Act
        page.Title = "Algorithms";
        page.Description = "Graphs";
        page.Subject = "   ";

        // Assert
        page.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    // The same limit the API's value object enforces, so a request that cannot succeed is not sent.
    [Fact]
    public void Save_Should_BeRefused_WhenTheTitleIsLongerThanTheServerAllows()
    {
        // Arrange
        ExamEditorViewModel page = CreatePage(null);

        // Act
        page.Title = new string('x', ExamLimits.TitleMaxLength + 1);
        page.Description = "Graphs";
        page.Subject = "ADS";

        // Assert
        page.SaveCommand.CanExecute(null).ShouldBeFalse();

        page.Title = new string('x', ExamLimits.TitleMaxLength);
        page.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Save_Should_StayOnTheForm_AndShowWhatTheServerSaid()
    {
        // Arrange
        _exams.CreateExamAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<Guid>(new ApiError(400, "Exams.Validation", "Title is required.", [])));
        ExamEditorViewModel page = CreatePage(null);
        page.Title = "Algorithms";
        page.Description = "Graphs";
        page.Subject = "ADS";

        // Act
        await page.SaveCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("Title is required.");
        _navigation.DidNotReceive().NavigateTo<ProfessorHomeViewModel>();
    }

    [Fact]
    public async Task Load_Should_ShowTheServersMessage_WhenTheExamCannotBeRead()
    {
        // Arrange
        var examId = Guid.NewGuid();
        _exams.GetExamAsync(examId, Arg.Any<CancellationToken>()).Returns(
            ApiResult.Failure<ExamDetails>(new ApiError(404, "Exams.NotFound", "The exam was not found", [])));
        ExamEditorViewModel page = CreatePage(examId);

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("The exam was not found");
        page.IsReadOnly.ShouldBeTrue();
        page.SaveCommand.CanExecute(null).ShouldBeFalse();
    }
}
