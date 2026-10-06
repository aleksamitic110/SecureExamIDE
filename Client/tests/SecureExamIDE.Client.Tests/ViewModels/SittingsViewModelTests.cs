using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Professor;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class SittingsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ExamId = Guid.NewGuid();

    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IProfessorExams _exams = Substitute.For<IProfessorExams>();
    private readonly FakeTimeProvider _time = new(Now);

    private SittingsViewModel CreatePage(Guid? examId = null, bool canSchedule = false)
    {
        var page = new SittingsViewModel(_session, _navigation, _exams, _time);
        page.Initialize(examId, examId is null ? null : "Algorithms", canSchedule);
        return page;
    }

    private static MySitting Sitting(DateTimeOffset startsAt, bool cancelled = false, int submissions = 0) =>
        new(Guid.NewGuid(), ExamId, "Algorithms", startsAt, startsAt.AddHours(2), cancelled, submissions, Now.AddDays(-1));

    private void Returns(params MySitting[] items) =>
        _exams.GetMySittingsAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new PagedList<MySitting>(items, 1, 20, items.Length, false, false)));

    [Fact]
    public async Task Load_Should_ShowTheStatusOfEachSitting()
    {
        // Arrange
        Returns(
            Sitting(Now.AddDays(3)),
            Sitting(Now.AddMinutes(-30)),
            Sitting(Now.AddDays(-5)),
            Sitting(Now.AddDays(1), cancelled: true));
        SittingsViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Sittings.Select(s => s.Status).ShouldBe(["Upcoming", "In progress", "Ended", "Cancelled"]);
    }

    // Cancelled sittings are part of the history; the student's view of an exam hides them, this one
    // does not.
    [Fact]
    public async Task Load_Should_OfferCancelling_OnEverythingNotAlreadyCancelled()
    {
        // Arrange
        Returns(Sitting(Now.AddDays(-5)), Sitting(Now.AddDays(1), cancelled: true));
        SittingsViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Sittings[0].CanCancel.ShouldBeTrue("cancelling an ended sitting is how late uploads are stopped");
        page.Sittings[1].CanCancel.ShouldBeFalse();
    }

    [Fact]
    public async Task OneExam_Should_AskForThatExamOnly()
    {
        // Arrange
        Returns();
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().GetMySittingsAsync(1, ExamId, Arg.Any<CancellationToken>());
        page.Heading.ShouldBe("Sittings of Algorithms");
        page.CanSchedule.ShouldBeTrue();
    }

    [Fact]
    public async Task AllSittings_Should_AskWithoutAnExamFilter()
    {
        // Arrange
        Returns();
        SittingsViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().GetMySittingsAsync(1, null, Arg.Any<CancellationToken>());
        page.Heading.ShouldBe("All your sittings");
        page.CanSchedule.ShouldBeFalse();
    }

    // The API seals the package from the finished set of files, so a draft cannot have a sitting.
    [Fact]
    public void ADraft_Should_NotOfferScheduling()
    {
        // Act
        SittingsViewModel page = CreatePage(ExamId, canSchedule: false);

        // Assert
        page.CanSchedule.ShouldBeFalse();
        page.Introduction.ShouldContain("published exam");
    }

    [Fact]
    public async Task BeginSchedule_Should_SuggestTheNextWholeHourForTwoHours()
    {
        // Arrange
        Returns();
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        page.BeginScheduleCommand.Execute(null);

        // Assert
        page.IsScheduling.ShouldBeTrue();
        page.StartTime.ShouldBe(TimeSpan.FromHours(13));
        page.EndTime.ShouldBe(TimeSpan.FromHours(15));
    }

    // The form's calendar and its hour and minute lists are another view of the same start and end.
    [Fact]
    public async Task TheFormsDayHourAndMinute_Should_SetTheStartAndEnd()
    {
        // Arrange
        Returns();
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);
        await page.LoadCommand.ExecuteAsync(null);
        page.BeginScheduleCommand.Execute(null);

        // Act
        page.StartDay = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Unspecified);
        page.StartHour = 9;
        page.StartMinute = 30;
        page.EndDay = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Unspecified);
        page.EndHour = 11;
        page.EndMinute = 45;

        // Assert
        page.StartDate!.Value.Date.ShouldBe(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Unspecified));
        page.StartTime.ShouldBe(new TimeSpan(9, 30, 0));
        page.EndTime.ShouldBe(new TimeSpan(11, 45, 0));
        page.StartDay.ShouldBe(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Unspecified));
        page.StartHour.ShouldBe(9);
        page.EndMinute.ShouldBe(45);
        page.Hours.Count.ShouldBe(24);
        page.Minutes.ShouldContain(55);
    }

    [Fact]
    public async Task Schedule_Should_SendTheChosenTimes_AndShowTheCodeOnce()
    {
        // Arrange
        Returns();
        _exams.ScheduleSittingAsync(ExamId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new ScheduledSitting(
                Guid.NewGuid(), "A2JR3FD3ANTC8DGVAES1", Now.AddHours(1), Now.AddHours(3), 2048, new string('a', 64))));
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);
        await page.LoadCommand.ExecuteAsync(null);
        page.BeginScheduleCommand.Execute(null);

        // Act
        await page.ScheduleCommand.ExecuteAsync(null);

        // Assert
        await _exams.Received().ScheduleSittingAsync(
            ExamId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        page.IsScheduling.ShouldBeFalse();
        page.HasNewCode.ShouldBeTrue();
        page.NewCode.ShouldBe("A2JR-3FD3-ANTC-8DGV-AES1");

        // Dismissing it is the end of it: nothing can show the code again.
        page.DismissCodeCommand.Execute(null);
        page.HasNewCode.ShouldBeFalse();
        page.NewCode.ShouldBeNull();
    }

    // The same two rules the API applies, so an impossible sitting is never sent.
    [Fact]
    public async Task Schedule_Should_RefuseASittingThatEndsBeforeItStarts()
    {
        // Arrange
        Returns();
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);
        await page.LoadCommand.ExecuteAsync(null);
        page.BeginScheduleCommand.Execute(null);
        page.EndDate = page.StartDate;
        page.EndTime = page.StartTime - TimeSpan.FromHours(1);

        // Act
        await page.ScheduleCommand.ExecuteAsync(null);

        // Assert
        page.ScheduleError.ShouldBe("The sitting has to end after it starts.");
        await _exams.DidNotReceive().ScheduleSittingAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Schedule_Should_RefuseASittingThatIsAlreadyOver()
    {
        // Arrange
        Returns();
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);
        await page.LoadCommand.ExecuteAsync(null);
        page.BeginScheduleCommand.Execute(null);
        page.StartDate = Now.AddDays(-2);
        page.EndDate = Now.AddDays(-2);

        // Act
        await page.ScheduleCommand.ExecuteAsync(null);

        // Assert
        page.ScheduleError.ShouldBe("The sitting has to end in the future.");
        await _exams.DidNotReceive().ScheduleSittingAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Schedule_Should_ShowTheServersMessage_WhenItRefuses()
    {
        // Arrange
        Returns();
        _exams.ScheduleSittingAsync(ExamId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<ScheduledSitting>(new ApiError(
                409, "ExamSessions.ExamNotPublished", "A session can only be scheduled for a published exam", [])));
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);
        await page.LoadCommand.ExecuteAsync(null);
        page.BeginScheduleCommand.Execute(null);

        // Act
        await page.ScheduleCommand.ExecuteAsync(null);

        // Assert
        page.ScheduleError.ShouldBe("A session can only be scheduled for a published exam");
        page.HasNewCode.ShouldBeFalse();
        page.IsScheduling.ShouldBeTrue("the form stays open so the professor can correct it");
    }

    [Fact]
    public async Task Cancelling_Should_AskFirst_AndCancelOnlyWhenConfirmed()
    {
        // Arrange
        MySitting sitting = Sitting(Now.AddDays(1));
        Returns(sitting);
        _exams.CancelSittingAsync(sitting.Id, Arg.Any<CancellationToken>()).Returns(ApiResult.Success());
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);
        await page.LoadCommand.ExecuteAsync(null);

        // Act
        page.AskToCancelSittingCommand.Execute(page.Sittings[0]);

        // Assert
        page.IsConfirming.ShouldBeTrue();
        page.ConfirmQuestion!.ShouldContain("cannot be un-cancelled");
        await _exams.DidNotReceive().CancelSittingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await page.ConfirmCommand.ExecuteAsync(null);

        await _exams.Received().CancelSittingAsync(sitting.Id, Arg.Any<CancellationToken>());
        page.StatusMessage.ShouldBe("The sitting was cancelled.");
    }

    [Fact]
    public async Task Cancelling_Should_DoNothing_WhenTheQuestionIsDeclined()
    {
        // Arrange
        Returns(Sitting(Now.AddDays(1)));
        SittingsViewModel page = CreatePage(ExamId, canSchedule: true);
        await page.LoadCommand.ExecuteAsync(null);
        page.AskToCancelSittingCommand.Execute(page.Sittings[0]);

        // Act
        page.CancelConfirmCommand.Execute(null);
        await page.ConfirmCommand.ExecuteAsync(null);

        // Assert
        await _exams.DidNotReceive().CancelSittingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_Should_ShowTheServersMessage_WhenTheListCannotBeRead()
    {
        // Arrange
        _exams.GetMySittingsAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<PagedList<MySitting>>(ApiError.Unreachable("Cannot reach the server.")));
        SittingsViewModel page = CreatePage();

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("Cannot reach the server.");
        page.IsEmpty.ShouldBeFalse();
    }
}
