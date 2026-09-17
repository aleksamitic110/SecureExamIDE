using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.ExamDay;

namespace SecureExamIDE.Client.ViewModels.Account;

// The first page: decides from the stored credential where the user belongs. A computer that is
// already bound to an account goes straight in, without anyone typing a password.
public sealed partial class StartupViewModel(ISessionService session, INavigationService navigation) : ViewModelBase
{
    [ObservableProperty]
    private string _status = ConnectingMessage;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private bool _canWorkOffline;

    [RelayCommand]
    private async Task StartAsync()
    {
        CanRetry = false;
        CanWorkOffline = false;
        Status = ConnectingMessage;

        StartupResult result = await session.RestoreAsync();

        switch (result.Outcome)
        {
            case StartupOutcome.SignedIn:
                navigation.NavigateToHome(session.CurrentUser!);
                break;

            case StartupOutcome.EmailNotVerified:
                navigation.NavigateTo<VerifyEmailViewModel>(page =>
                    page.Initialize(result.Email!, session.CompleteSignInAsync, codeWasJustSent: false));
                break;

            case StartupOutcome.SignedOut:
                navigation.NavigateTo<WelcomeViewModel>();
                break;

            default:
                Status = result.Error?.Message ?? "The server could not be reached.";
                CanRetry = true;
                CanWorkOffline = result.CanWorkOffline;
                break;
        }
    }

    // In the exam room there may be no connection at all; what was downloaded at home still opens.
    [RelayCommand]
    private async Task ContinueOfflineAsync()
    {
        if (await session.ContinueOfflineAsync())
        {
            navigation.NavigateTo<DownloadedExamsViewModel>();
        }
    }

    private const string ConnectingMessage = "Connecting to the server…";
}
