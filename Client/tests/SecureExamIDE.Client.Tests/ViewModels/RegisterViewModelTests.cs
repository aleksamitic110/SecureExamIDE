using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Account;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class RegisterViewModelTests
{
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    private RegisterViewModel CreateFilledIn() => new(_session, _navigation)
    {
        FirstName = "Ana",
        LastName = "Anic",
        Email = " ana@example.com ",
        Password = "Password123",
        ConfirmPassword = "Password123",
        IndexNumber = "19252",
        DeviceName = "ana-laptop"
    };

    [Fact]
    public async Task Register_Should_SendTheStudentsDetails_AndOpenTheCodeScreen()
    {
        // Arrange
        _session.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success());
        RegisterViewModel page = CreateFilledIn();

        // Act
        await page.RegisterCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBeNull();
        await _session.Received(1).RegisterAsync(
            Arg.Is<RegisterRequest>(r =>
                r.Email == "ana@example.com" &&
                r.Role == UserRole.Student &&
                r.IndexNumber == "19252" &&
                r.ProfessorRegistrationCode == null),
            Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo(Arg.Any<Action<VerifyEmailViewModel>?>());
    }

    [Fact]
    public async Task Register_Should_SendTheCodeButNoIndexNumber_ForAProfessor()
    {
        // Arrange
        _session.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success());
        RegisterViewModel page = CreateFilledIn();
        page.IsProfessor = true;
        page.ProfessorRegistrationCode = "the-code";

        // Act
        await page.RegisterCommand.ExecuteAsync(null);

        // Assert
        await _session.Received(1).RegisterAsync(
            Arg.Is<RegisterRequest>(r =>
                r.Role == UserRole.Professor && r.IndexNumber == null && r.ProfessorRegistrationCode == "the-code"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("student-without-index")]
    [InlineData("professor-without-code")]
    [InlineData("passwords-differ")]
    [InlineData("password-too-short")]
    public async Task Register_Should_StopBeforeCallingTheServer_WhenTheFormIsIncomplete(string problem)
    {
        // Arrange
        RegisterViewModel page = CreateFilledIn();

        switch (problem)
        {
            case "student-without-index":
                page.IndexNumber = "";
                break;
            case "professor-without-code":
                page.IsProfessor = true;
                break;
            case "passwords-differ":
                page.ConfirmPassword = "Password124";
                break;
            default:
                page.Password = page.ConfirmPassword = "short";
                break;
        }

        // Act
        await page.RegisterCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        await _session.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default);
    }

    [Fact]
    public async Task Register_Should_ShowTheServersMessage_WhenItRefuses()
    {
        // Arrange
        _session.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure(new ApiError(409, "Users.EmailNotUnique", "The provided email is not unique", [])));
        RegisterViewModel page = CreateFilledIn();

        // Act
        await page.RegisterCommand.ExecuteAsync(null);

        // Assert
        page.ErrorMessage.ShouldBe("The provided email is not unique");
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo<VerifyEmailViewModel>();
    }
}
