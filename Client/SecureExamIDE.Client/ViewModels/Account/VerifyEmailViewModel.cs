using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.ViewModels.Account;

// Takes the six-digit code from the e-mail. What a successful verification leads to depends on how
// the user got here, so the caller passes it in: after registering, the session starts with the
// credential already stored; after a login that was refused for an unverified address, the same
// login is repeated.
public sealed partial class VerifyEmailViewModel(
    ISessionService session,
    INavigationService navigation,
    TimeProvider timeProvider) : ViewModelBase, IDisposable
{
    private Func<CancellationToken, Task<ApiResult>> _continueAfterVerification = _ => Task.FromResult(ApiResult.Success());
    private CancellationTokenSource? _cooldown;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _infoMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand), nameof(ResendCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResendLabel))]
    [NotifyCanExecuteChangedFor(nameof(ResendCommand))]
    private int _resendSecondsLeft;

    public string ResendLabel => ResendSecondsLeft > 0
        ? $"Send a new code ({ResendSecondsLeft} s)"
        : "Send a new code";

    public void Initialize(
        string email,
        Func<CancellationToken, Task<ApiResult>> continueAfterVerification,
        bool codeWasJustSent)
    {
        Email = email;
        _continueAfterVerification = continueAfterVerification;

        InfoMessage = codeWasJustSent
            ? $"A 6-digit code was sent to {email}. It is valid for 15 minutes."
            : $"Enter the 6-digit code sent to {email}, or ask for a new one.";

        if (codeWasJustSent)
        {
            StartCooldown();
        }
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        string code = Code.Trim();

        if (code.Length != CodeLength || !code.All(char.IsAsciiDigit))
        {
            ErrorMessage = "The code is the 6 digits from the e-mail.";
            return;
        }

        ErrorMessage = null;
        IsBusy = true;

        try
        {
            ApiResult verified = await session.VerifyEmailAsync(Email, code);

            if (!verified.IsSuccess)
            {
                ErrorMessage = verified.Error.Message;
                return;
            }

            ApiResult signedIn = await _continueAfterVerification(CancellationToken.None);

            if (!signedIn.IsSuccess)
            {
                ErrorMessage = signedIn.Error.Message;
                return;
            }

            navigation.NavigateToHome(session.CurrentUser!);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanVerify() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanResend))]
    private async Task ResendAsync()
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            ApiResult sent = await session.ResendVerificationCodeAsync(Email);

            if (!sent.IsSuccess)
            {
                ErrorMessage = sent.Error.Message;
                return;
            }

            InfoMessage = $"A new code is on its way to {Email}. Earlier codes no longer work.";
            StartCooldown();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanResend() => !IsBusy && ResendSecondsLeft == 0;

    [RelayCommand]
    private void Back() => navigation.NavigateTo<WelcomeViewModel>();

    public void Dispose()
    {
        _cooldown?.Cancel();
        _cooldown?.Dispose();
        _cooldown = null;
    }

    // Mirrors the server's 60-second resend cooldown, so the button is not offered while a resend
    // would be silently ignored.
    private void StartCooldown()
    {
        Dispose();
        _cooldown = new CancellationTokenSource();
        _ = CountDownAsync(_cooldown.Token);
    }

    private async Task CountDownAsync(CancellationToken cancellationToken)
    {
        ResendSecondsLeft = ResendCooldownSeconds;

        try
        {
            while (ResendSecondsLeft > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
                ResendSecondsLeft--;
            }
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer countdown, or the page was left.
        }
    }

    private const int CodeLength = 6;
    private const int ResendCooldownSeconds = 60;
}
