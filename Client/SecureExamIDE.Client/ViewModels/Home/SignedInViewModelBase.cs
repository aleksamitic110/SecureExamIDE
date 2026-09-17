using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Account;

namespace SecureExamIDE.Client.ViewModels.Home;

// What every signed-in screen shares: who is signed in, and a way to sign this computer out.
public abstract partial class SignedInViewModelBase(ISessionService session, INavigationService navigation) : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    private bool _isBusy;

    public string Greeting => session.CurrentUser is { } user
        ? $"Welcome, {user.FirstName} {user.LastName}"
        : "Welcome";

    protected ISessionService Session => session;

    protected INavigationService Navigation => navigation;

    // Signing out forgets this computer: its credential is revoked and deleted, so getting back in
    // means signing in with the password again.
    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        IsBusy = true;

        try
        {
            await session.SignOutAsync();
            navigation.NavigateTo<WelcomeViewModel>();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Signing out revokes the credential on the server; offline that cannot happen, and deleting the
    // only local copy in an exam room would leave the student with no way in.
    private bool CanSignOut() => !IsBusy && !session.IsOffline;

    public bool IsOnline => !session.IsOffline;
}
