using CommunityToolkit.Mvvm.ComponentModel;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.ViewModels;

// The window's only state: which page it is showing. INavigationService is what changes it.
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopBarShown))]
    private ViewModelBase? _currentPage;

    // The bar with the application's name runs across the top of every signed-in page. The screens
    // that stand alone carry the name themselves, and the exam workspace has a header of its own -
    // an exam gets the whole window.
    public bool IsTopBarShown => CurrentPage is SignedInViewModelBase;
}
