using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Professor;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class ProfessorHomeViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IProfessorExams _exams = Substitute.For<IProfessorExams>();

    private ProfessorHomeViewModel CreatePage() => new(_session, _navigation, _exams);

    private static MyExam Exam(string title, ExamStatus status, DateTimeOffset? publishedAt = null) =>
        new(Guid.NewGuid(), title, "Algorithms and Data Structures", status, Now.AddDays(-2), publishedAt, 2, 1, 0);

    private void Returns(params MyExam[] items) =>
        _exams.GetMyExamsAsync(Arg.Any<int>(), Arg.Any<ExamStatus?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new PagedList<MyExam>(items, 1, 20, items.Length, false, false)));

    [Fact]
    public async Task Load_Should_ShowTheProfessorsOwnExams_DraftsIncluded()
    {
        // Arrange
        Returns(Exam("Algorithms", ExamStatus.Draft), Exam("Operating Systems", ExamStatus.Published, Now.AddDays(-1)));
        ProfessorHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Exams.Select(e => e.Title).ShouldBe(["Algorithms", "Operating Systems"]);
        page.Exams[0].Status.ShouldBe("Draft");
        page.IsEmpty.ShouldBeFalse();
        page.ErrorMessage.ShouldBeNull();
    }

    // Only a draft can still be corrected or thrown away, so those are the only rows that offer it.
    [Fact]
    public async Task Load_Should_OfferEditAndDelete_OnDraftsOnly()
    {
        // Arrange
        Returns(Exam("Algorithms", ExamStatus.Draft), Exam("Operating Systems", ExamStatus.Published, Now));
        ProfessorHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Exams[0].IsDraft.ShouldBeTrue();
        page.Exams[1].IsDraft.ShouldBeFalse();
    }

    [Fact]
    public async Task Load_Should_ShowTheServersMessage_WhenTheListCannotBeRead()
    {
        // Arrange
        _exams.GetMyExamsAsync(Arg.Any<int>(), Arg.Any<ExamStatus?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<PagedList<MyExam>>(ApiError.Unreachable("Cannot reach the server.")));
        ProfessorHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("Cannot reach the server.");
        page.Exams.ShouldBeEmpty();
        page.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task Load_Should_SayTheListIsEmpty_WhenTheProfessorHasNoExams()
    {
        // Arrange
        Returns();
        ProfessorHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.IsEmpty.ShouldBeTrue();
    }

    // Page 3 of the drafts has nothing to do with page 3 of everything.
    [Fact]
    public async Task ChangingTheFilter_Should_AskForTheFirstPageOfThatStatus()
    {
        // Arrange
        Returns(Exam("Algorithms", ExamStatus.Draft));
        ProfessorHomeViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        page.SelectedStatus = ExamStatusOption.Options.Single(o => o.Value == ExamStatus.Draft);
        await page.LoadPageCommand.ExecuteAsync(1);

        // Assert
        await _exams.Received().GetMyExamsAsync(1, ExamStatus.Draft, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_Should_AskWithoutAFilter_WhenAllIsChosen()
    {
        // Arrange
        Returns();
        ProfessorHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().GetMyExamsAsync(1, null, Arg.Any<CancellationToken>());
    }

    // Deleting a draft destroys its task files and toolchains, so it is asked about first.
    [Fact]
    public async Task Delete_Should_AskBeforeItDeletesAnything()
    {
        // Arrange
        Returns(Exam("Algorithms", ExamStatus.Draft));
        ProfessorHomeViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        page.AskToDeleteCommand.Execute(page.Exams[0]);

        // Assert
        page.IsConfirmingDelete.ShouldBeTrue();
        page.DeleteQuestion.ShouldContain("Algorithms");
        await _exams.DidNotReceive().DeleteExamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingTheQuestion_Should_LeaveTheDraftAlone()
    {
        // Arrange
        Returns(Exam("Algorithms", ExamStatus.Draft));
        ProfessorHomeViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        page.AskToDeleteCommand.Execute(page.Exams[0]);

        // Act
        page.CancelDeleteCommand.Execute(null);

        // Assert
        page.IsConfirmingDelete.ShouldBeFalse();
        await _exams.DidNotReceive().DeleteExamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingTheQuestion_Should_DeleteTheDraft_AndReadThePageBack()
    {
        // Arrange
        MyExam draft = Exam("Algorithms", ExamStatus.Draft);
        Returns(draft);
        _exams.DeleteExamAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(ApiResult.Success());
        ProfessorHomeViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        page.AskToDeleteCommand.Execute(page.Exams[0]);

        // Act
        await page.ConfirmDeleteCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().DeleteExamAsync(draft.Id, Arg.Any<CancellationToken>());
        page.IsConfirmingDelete.ShouldBeFalse();
        page.StatusMessage!.ShouldContain("Algorithms");
        // Once to load, once after the delete: the server decides what is left on the page.
        await _exams.Received(2).GetMyExamsAsync(Arg.Any<int>(), Arg.Any<ExamStatus?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingTheQuestion_Should_ShowTheServersMessage_WhenTheDeleteIsRefused()
    {
        // Arrange
        MyExam draft = Exam("Algorithms", ExamStatus.Draft);
        Returns(draft);
        _exams.DeleteExamAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(
            ApiResult.Failure(new ApiError(409, "Exams.NotDraft", "An exam can only be changed while it is a draft", [])));
        ProfessorHomeViewModel page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        page.AskToDeleteCommand.Execute(page.Exams[0]);

        // Act
        await page.ConfirmDeleteCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("An exam can only be changed while it is a draft");
        page.StatusMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Paging_Should_FollowWhatTheServerReports()
    {
        // Arrange
        _exams.GetMyExamsAsync(Arg.Any<int>(), Arg.Any<ExamStatus?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new PagedList<MyExam>(
                [Exam("Algorithms", ExamStatus.Draft)], 2, 20, 45, true, true)));
        ProfessorHomeViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Page.ShouldBe(2);
        page.PageCount.ShouldBe(3);
        page.PageLabel.ShouldBe("Page 2 of 3");
        page.NextPageCommand.CanExecute(null).ShouldBeTrue();
        page.PreviousPageCommand.CanExecute(null).ShouldBeTrue();
    }
}
