using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Navigation;

namespace SecureExamIDE.Client.ViewModels.Account;

// Shown when this computer is not bound to any account yet.
public sealed partial class WelcomeViewModel(INavigationService navigation) : ViewModelBase
{
    [RelayCommand]
    private void CreateAccount() => navigation.NavigateTo<RegisterViewModel>();

    [RelayCommand]
    private void SignIn() => navigation.NavigateTo<LoginViewModel>();
}
