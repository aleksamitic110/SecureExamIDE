using CommunityToolkit.Mvvm.Input;

namespace SecureExamIDE.Client.Services.Navigation;

// A page that fetches its content when it is shown. Navigation starts the load, so a page never
// appears empty waiting for a view event to ask for it.
public interface ILoadablePage
{
    IAsyncRelayCommand LoadCommand { get; }
}
