using Microsoft.Extensions.DependencyInjection;
using SecureExamIDE.Client.ViewModels;

namespace SecureExamIDE.Client.Services.Navigation;

internal sealed class NavigationService(IServiceProvider services, MainWindowViewModel shell) : INavigationService
{
    public TViewModel NavigateTo<TViewModel>(Action<TViewModel>? configure = null)
        where TViewModel : ViewModelBase
    {
        TViewModel page = services.GetRequiredService<TViewModel>();
        configure?.Invoke(page);

        // A page that holds a timer or a cancellation source lets it go when it is left.
        if (shell.CurrentPage is IDisposable previous)
        {
            previous.Dispose();
        }

        shell.CurrentPage = page;

        if (page is ILoadablePage loadable)
        {
            loadable.LoadCommand.Execute(null);
        }

        return page;
    }
}
