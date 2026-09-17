using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Account;
using SecureExamIDE.Client.ViewModels.ExamDay;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class StartupViewModelTests
{
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    [Fact]
    public async Task Start_Should_OpenTheWelcomeScreen_WhenThisComputerIsNotSignedIn()
    {
        // Arrange
        _session.RestoreAsync(Arg.Any<CancellationToken>()).Returns(new StartupResult(StartupOutcome.SignedOut));
        var page = new StartupViewModel(_session, _navigation);

        // Act
        await page.StartCommand.ExecuteAsync(null);

        // Assert
        _navigation.Received(1).NavigateTo(Arg.Any<Action<WelcomeViewModel>?>());
    }

    [Fact]
    public async Task Start_Should_GoStraightToTheStudentHome_WhenAlreadySignedIn()
    {
        // Arrange
        _session.RestoreAsync(Arg.Any<CancellationToken>()).Returns(new StartupResult(StartupOutcome.SignedIn));
        _session.CurrentUser.Returns(new UserProfile(Guid.NewGuid(), "ana@example.com", "Ana", "Anic", UserRole.Student, "19252"));
        var page = new StartupViewModel(_session, _navigation);

        // Act
        await page.StartCommand.ExecuteAsync(null);

        // Assert
        _navigation.Received(1).NavigateTo(Arg.Any<Action<StudentHomeViewModel>?>());
    }

    [Fact]
    public async Task Start_Should_OfferARetry_WhenTheServerCannotBeReached()
    {
        // Arrange
        _session.RestoreAsync(Arg.Any<CancellationToken>()).Returns(new StartupResult(
            StartupOutcome.Failed, "ana@example.com", ApiError.Unreachable("Cannot reach the server.")));
        var page = new StartupViewModel(_session, _navigation);

        // Act
        await page.StartCommand.ExecuteAsync(null);

        // Assert
        page.CanRetry.ShouldBeTrue();
        page.Status.ShouldBe("Cannot reach the server.");
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo<WelcomeViewModel>();
    }

    [Fact]
    public async Task Start_Should_OfferToContinueOffline_AndOpenTheDownloadedExams()
    {
        // Arrange
        _session.RestoreAsync(Arg.Any<CancellationToken>()).Returns(new StartupResult(
            StartupOutcome.Failed, "ana@example.com", ApiError.Unreachable("Cannot reach the server."), CanWorkOffline: true));
        _session.ContinueOfflineAsync(Arg.Any<CancellationToken>()).Returns(true);
        var page = new StartupViewModel(_session, _navigation);

        // Act
        await page.StartCommand.ExecuteAsync(null);
        await page.ContinueOfflineCommand.ExecuteAsync(null);

        // Assert
        page.CanWorkOffline.ShouldBeTrue();
        _navigation.Received(1).NavigateTo(Arg.Any<Action<DownloadedExamsViewModel>?>());
    }
}
