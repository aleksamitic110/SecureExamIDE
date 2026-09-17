using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.ExamDay;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class DownloadedExamsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly ILocalExamLibrary _library = Substitute.For<ILocalExamLibrary>();

    private static DownloadedSitting SittingAt(DateTimeOffset startsAt) =>
        new(Guid.NewGuid(), startsAt, startsAt.AddHours(2), 176, new string('f', 64), Now.AddDays(-3));

    [Fact]
    public async Task Load_Should_PutTheSittingInProgressFirst_AndEndedOnesLast()
    {
        // Arrange
        DownloadedSitting ended = SittingAt(Now.AddDays(-10));
        DownloadedSitting upcoming = SittingAt(Now.AddDays(5));
        DownloadedSitting inProgress = SittingAt(Now.AddMinutes(-30));
        _library.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([
            new DownloadedExam(Guid.NewGuid(), "Compilers", "", "", "", [ended, upcoming], [], Now),
            new DownloadedExam(Guid.NewGuid(), "Algorithms", "", "", "", [inProgress], [], Now)
        ]);
        var page = new DownloadedExamsViewModel(_session, _navigation, _library, new FakeTimeProvider(Now));

        // Act
        await page.LoadCommand.ExecuteAsync(null);

        // Assert
        page.Sittings.Select(s => s.Status).ShouldBe(["In progress", "Upcoming", "Ended"]);
        page.Sittings[0].ExamTitle.ShouldBe("Algorithms");
    }

    // Offline there is no server to revoke the credential with, so signing out is not offered.
    [Fact]
    public void Offline_Should_NotOfferSigningOut()
    {
        // Arrange
        _session.IsOffline.Returns(true);

        // Act
        var page = new DownloadedExamsViewModel(_session, _navigation, _library, new FakeTimeProvider(Now));

        // Assert
        page.IsOnline.ShouldBeFalse();
        page.SignOutCommand.CanExecute(null).ShouldBeFalse();
        page.ConnectionNote.ShouldStartWith("Working offline");
    }
}
