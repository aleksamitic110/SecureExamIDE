using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.ViewModels.Account;

// Binds this computer to an account that already exists - a student's second laptop, for example.
// It is the only place a password is typed after registration.
public sealed partial class LoginViewModel(ISessionService session, INavigationService navigation) : ViewModelBase
{
    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _deviceName = Environment.MachineName;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || Password.Length == 0)
        {
            ErrorMessage = "Enter your e-mail address and password.";
            return;
        }

        string email = Email.Trim();
        string password = Password;
        string deviceName = DeviceName.Trim();

        ErrorMessage = null;
        IsBusy = true;

        try
        {
            ApiResult result = await session.LoginAsync(email, password, deviceName);

            if (result.IsSuccess)
            {
                navigation.NavigateToHome(session.CurrentUser!);
                return;
            }

            switch (result.Error.Code)
            {
                // The password was right; the address has just not been confirmed yet. Once the code
                // is entered, the same sign-in is repeated.
                case ErrorCodes.EmailNotVerified:
                    navigation.NavigateTo<VerifyEmailViewModel>(page => page.Initialize(
                        email,
                        cancellationToken => session.LoginAsync(email, password, deviceName, cancellationToken),
                        codeWasJustSent: false));
                    break;

                // The API gives an unknown address and a wrong password the same answer, on purpose.
                case ErrorCodes.UserNotFoundByEmail:
                    ErrorMessage = "The e-mail address or password is incorrect.";
                    break;

                default:
                    ErrorMessage = result.Error.Message;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSignIn() => !IsBusy;

    [RelayCommand]
    private void Back() => navigation.NavigateTo<WelcomeViewModel>();
}
