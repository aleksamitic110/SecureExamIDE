using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.ViewModels.Account;

// Creates an account and binds this computer to it. For the first version the user picks the role
// here, and a professor enters the registration code; later the faculty e-mail domain will decide
// the role, and the choice and the code field will go.
public sealed partial class RegisterViewModel(ISessionService session, INavigationService navigation) : ViewModelBase
{
    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfessor))]
    private bool _isStudent = true;

    [ObservableProperty]
    private string _indexNumber = string.Empty;

    [ObservableProperty]
    private string _professorRegistrationCode = string.Empty;

    [ObservableProperty]
    private string _deviceName = Environment.MachineName;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private bool _isBusy;

    public bool IsProfessor
    {
        get => !IsStudent;
        set => IsStudent = !value;
    }

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        ErrorMessage = Validate();

        if (ErrorMessage is not null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            string email = Email.Trim();

            var request = new RegisterRequest(
                email,
                FirstName.Trim(),
                LastName.Trim(),
                Password,
                IsStudent ? UserRole.Student : UserRole.Professor,
                IsStudent ? IndexNumber.Trim() : null,
                IsStudent ? null : ProfessorRegistrationCode.Trim(),
                DeviceName.Trim());

            ApiResult result = await session.RegisterAsync(request);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            // The credential is already stored; the session starts once the mailed code comes back.
            navigation.NavigateTo<VerifyEmailViewModel>(page =>
                page.Initialize(email, session.CompleteSignInAsync, codeWasJustSent: true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRegister() => !IsBusy;

    [RelayCommand]
    private void Back() => navigation.NavigateTo<WelcomeViewModel>();

    // Only what can be checked without the server. The API validates everything again, and its
    // messages are shown when it disagrees.
    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
        {
            return "Enter your first and last name.";
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            return "Enter your e-mail address.";
        }

        if (Password.Length < MinimumPasswordLength)
        {
            return $"The password must be at least {MinimumPasswordLength} characters long.";
        }

        if (Password != ConfirmPassword)
        {
            return "The two passwords do not match.";
        }

        if (IsStudent && string.IsNullOrWhiteSpace(IndexNumber))
        {
            return "Enter your index number.";
        }

        if (IsProfessor && string.IsNullOrWhiteSpace(ProfessorRegistrationCode))
        {
            return "Enter the professor registration code.";
        }

        return string.IsNullOrWhiteSpace(DeviceName) ? "Enter a name for this computer." : null;
    }

    private const int MinimumPasswordLength = 8;
}
