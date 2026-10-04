using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Review;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.ViewModels.Professor;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class SubmissionsViewModelTests
{
    private static readonly Guid SittingId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly ISubmissionReview _review = Substitute.For<ISubmissionReview>();
    private readonly ReviewKeyCache _keys = new(new FakeTimeProvider(Now));

    private static SubmissionSummary Handed(string firstName, bool late = false) => new(
        Guid.NewGuid(), Guid.NewGuid(), firstName, "Anic", "19252", "student@example.com",
        "ana-laptop", Now, late, 500, new string('a', 64), 200, new string('b', 64));

    private async Task<SubmissionsViewModel> OpenWithAsync(params SubmissionSummary[] submissions)
    {
        _review.GetSubmissionsAsync(SittingId, 1, Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new PagedList<SubmissionSummary>(submissions, 1, 50, submissions.Length, false, false)));

        var page = new SubmissionsViewModel(_session, _navigation, _review, _keys);
        page.Initialize(SittingId, "Algorithms", "Fri 18 Sep 2026 09:00 – 11:00");
        await page.LoadCommand.ExecuteAsync(null);

        return page;
    }

    private void CodeOpensTheSitting() =>
        _review.DeriveHandInKeyAsync(SittingId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(RandomNumberGenerator.GetBytes(32)));

    private void SubmissionOpens() =>
        _review.OpenAsync(SittingId, Arg.Any<SubmissionSummary>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new OpenedSubmission(
                [new ExamTaskFile("main.c", "int main(void){}"u8.ToArray())],
                new ActivityLogContents([], IsComplete: true, 0))));

    [Fact]
    public async Task Load_Should_ListWhoHandedIn()
    {
        // Act
        SubmissionsViewModel page = await OpenWithAsync(Handed("Ana"), Handed("Marko", late: true));

        // Assert
        page.Submissions.Select(item => item.Student).ShouldBe(["Ana Anic (19252)", "Marko Anic (19252)"]);
        page.Submissions[1].IsLate.ShouldBeTrue();
        page.IsEmpty.ShouldBeFalse();
    }

    // The code is asked for when the first solution is opened, and not again for the next one.
    [Fact]
    public async Task Open_Should_AskForTheCodeOnce_AndReuseItAfterwards()
    {
        // Arrange
        CodeOpensTheSitting();
        SubmissionOpens();
        SubmissionsViewModel page = await OpenWithAsync(Handed("Ana"), Handed("Marko"));

        // Act - the first one asks.
        await page.OpenCommand.ExecuteAsync(page.Submissions[0]);
        bool askedFirst = page.IsAskingForCode;

        page.Code = "B34K-X088-D12W-75Y6-MJQX";
        await page.ConfirmCodeCommand.ExecuteAsync(null);

        // The second one does not.
        await page.OpenCommand.ExecuteAsync(page.Submissions[1]);

        // Assert
        askedFirst.ShouldBeTrue();
        page.IsAskingForCode.ShouldBeFalse();
        page.IsSittingOpen.ShouldBeTrue();
        page.Code.ShouldBeEmpty();

        await _review.Received(1).DeriveHandInKeyAsync(SittingId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _navigation.Received(2).NavigateTo(Arg.Any<Action<SubmissionReviewViewModel>?>());
    }

    [Fact]
    public async Task Open_Should_KeepAskingWhenTheCodeIsWrong()
    {
        // Arrange
        _review.DeriveHandInKeyAsync(SittingId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<byte[]>(new ApiError(0, "Review.WrongCode", "This code does not belong to this sitting.", [])));

        SubmissionsViewModel page = await OpenWithAsync(Handed("Ana"));

        // Act
        await page.OpenCommand.ExecuteAsync(page.Submissions[0]);
        page.Code = "B34K-X088-D12W-75Y6-0000";
        await page.ConfirmCodeCommand.ExecuteAsync(null);

        // Assert
        page.IsAskingForCode.ShouldBeTrue();
        page.CodeError.ShouldBe("This code does not belong to this sitting.");
        page.IsSittingOpen.ShouldBeFalse();
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo<SubmissionReviewViewModel>();
    }

    // Going back to the sittings leaves this sitting, so the next visit asks for the code again.
    [Fact]
    public async Task Back_Should_ForgetTheCode()
    {
        // Arrange
        CodeOpensTheSitting();
        SubmissionOpens();
        SubmissionsViewModel page = await OpenWithAsync(Handed("Ana"));
        await page.OpenCommand.ExecuteAsync(page.Submissions[0]);
        page.Code = "B34K-X088-D12W-75Y6-MJQX";
        await page.ConfirmCodeCommand.ExecuteAsync(null);

        // Act
        page.BackCommand.Execute(null);

        // Assert
        _keys.Get(SittingId).ShouldBeNull();
        _navigation.Received(1).NavigateTo(Arg.Any<Action<SittingsViewModel>?>());
    }

    [Fact]
    public async Task Open_Should_ExplainWhenTheSubmissionCannotBeOpened()
    {
        // Arrange
        CodeOpensTheSitting();
        _review.OpenAsync(SittingId, Arg.Any<SubmissionSummary>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<OpenedSubmission>(new ApiError(0, "Download.LinkExpired", "The download link has expired.", [])));

        SubmissionsViewModel page = await OpenWithAsync(Handed("Ana"));

        // Act
        await page.OpenCommand.ExecuteAsync(page.Submissions[0]);
        page.Code = "B34K-X088-D12W-75Y6-MJQX";
        await page.ConfirmCodeCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("The download link has expired.");
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo<SubmissionReviewViewModel>();
    }
}
