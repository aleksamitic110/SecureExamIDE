using SecureExamIDE.Client.ViewModels;

namespace SecureExamIDE.Client.Services.Navigation;

// Switches the page the main window shows. A page is created fresh on every visit, and configure
// hands it what it needs before it appears - the e-mail address for the code screen, for example.
public interface INavigationService
{
    TViewModel NavigateTo<TViewModel>(Action<TViewModel>? configure = null)
        where TViewModel : ViewModelBase;
}
