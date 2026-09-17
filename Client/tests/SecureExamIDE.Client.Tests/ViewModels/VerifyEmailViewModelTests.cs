using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Account;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.Tests.ViewModels;

public sealed class VerifyEmailViewModelTests : IDisposable
{
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly FakeTimeProvider _time = new();
    private readonly VerifyEmailViewModel _page;
    private int _continuations;

    public VerifyEmailViewModelTests()
    {
        _page = new VerifyEmailViewModel(_session, _navigation, _time);
        _session.CurrentUser.Returns(new UserProfile(Guid.NewGuid(), "ana@example.com", "Ana", "Anic", UserRole.Student, "19252"));
    }

    public void Dispose() => _page.Dispose();

    private void Open(bool codeWasJustSent) => _page.Initialize(
        "ana@example.com",
        _ =>
        {
            _continuations++;
            return Task.FromResult(ApiResult.Success());
        },
        codeWasJustSent);

    [Fact]
    public async Task Verify_Should_RejectAnythingButSixDigits_WithoutCallingTheServer()
    {
        // Arrange
        Open(codeWasJustSent: false);
        _page.Code = "12a45";

        // Act
        await _page.VerifyCommand.ExecuteAsync(null);

        // Assert
        _page.ErrorMessage.ShouldNotBeNull();
        await _session.DidNotReceiveWithAnyArgs().VerifyEmailAsync(default!, default!, default);
    }

    [Fact]
    public async Task Verify_Should_Continue_AndGoHome_WhenTheCodeIsRight()
    {
        // Arrange
        _session.VerifyEmailAsync("ana@example.com", "123456", Arg.Any<CancellationToken>()).Returns(ApiResult.Success());
        Open(codeWasJustSent: true);
        _page.Code = " 123456 ";

        // Act
        await _page.VerifyCommand.ExecuteAsync(null);

        // Assert
        _continuations.ShouldBe(1);
        _navigation.Received(1).NavigateTo(Arg.Any<Action<StudentHomeViewModel>?>());
    }

    [Fact]
    public async Task Verify_Should_StayAndExplain_WhenTheCodeIsWrong()
    {
        // Arrange
        _session.VerifyEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure(new ApiError(400, "Users.InvalidVerificationCode", "The verification code is invalid or has expired.", [])));
        Open(codeWasJustSent: true);
        _page.Code = "000000";

        // Act
        await _page.VerifyCommand.ExecuteAsync(null);

        // Assert
        _page.ErrorMessage.ShouldBe("The verification code is invalid or has expired.");
        _continuations.ShouldBe(0);
    }

    // Mirrors the server's cooldown: a resend is not offered while it would be ignored.
    [Fact]
    public async Task Resend_Should_BeOfferedAgain_OnlyAfterAMinute()
    {
        // Arrange
        Open(codeWasJustSent: true);

        // Assert - just after the code went out.
        _page.ResendSecondsLeft.ShouldBe(60);
        _page.ResendCommand.CanExecute(null).ShouldBeFalse();

        // Act - one simulated second at a time.
        for (int second = 59; second >= 0; second--)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await WaitUntilAsync(() => _page.ResendSecondsLeft == second);
        }

        // Assert
        _page.ResendCommand.CanExecute(null).ShouldBeTrue();
        _page.ResendLabel.ShouldBe("Send a new code");
    }

    [Fact]
    public void Resend_Should_BeAvailableAtOnce_WhenNoCodeWasJustSent()
    {
        // Act
        Open(codeWasJustSent: false);

        // Assert
        _page.ResendCommand.CanExecute(null).ShouldBeTrue();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        condition().ShouldBeTrue();
    }
}
