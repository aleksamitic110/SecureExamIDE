using CommunityToolkit.Mvvm.ComponentModel;

namespace SecureExamIDE.Client.ViewModels;

// The window's only state: which page it is showing. INavigationService is what changes it.
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;
}
