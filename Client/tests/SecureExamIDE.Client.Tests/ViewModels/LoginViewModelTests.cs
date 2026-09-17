using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Account;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class LoginViewModelTests
{
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    private LoginViewModel CreateFilledIn() => new(_session, _navigation)
    {
        Email = "ana@example.com",
        Password = "Password123",
        DeviceName = "second-laptop"
    };

    [Fact]
    public async Task SignIn_Should_OpenTheProfessorHome_ForAProfessor()
    {
        // Arrange
        _session.LoginAsync("ana@example.com", "Password123", "second-laptop", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success());
        _session.CurrentUser.Returns(new UserProfile(Guid.NewGuid(), "ana@example.com", "Ana", "Anic", UserRole.Professor, null));

        LoginViewModel page = CreateFilledIn();

        // Act
        await page.SignInCommand.ExecuteAsync(null);

        // Assert
        _navigation.Received(1).NavigateTo(Arg.Any<Action<ProfessorHomeViewModel>?>());
    }

    // The password was right but the address is unconfirmed: the code screen, not an error.
    [Fact]
    public async Task SignIn_Should_OpenTheCodeScreen_WhenTheAddressIsNotVerified()
    {
        // Arrange
        _session.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure(new ApiError(403, ErrorCodes.EmailNotVerified, "Not verified.", [])));

        LoginViewModel page = CreateFilledIn();

        // Act
        await page.SignInCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBeNull();
        _navigation.Received(1).NavigateTo(Arg.Any<Action<VerifyEmailViewModel>?>());
    }

    [Fact]
    public async Task SignIn_Should_NotRevealWhetherTheAddressExists()
    {
        // Arrange
        _session.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure(new ApiError(404, ErrorCodes.UserNotFoundByEmail, "The user with the specified email was not found", [])));

        LoginViewModel page = CreateFilledIn();

        // Act
        await page.SignInCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("The e-mail address or password is incorrect.");
    }
}
