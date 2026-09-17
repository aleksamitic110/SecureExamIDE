using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.ViewModels;
using SecureExamIDE.Client.ViewModels.Account;
using SecureExamIDE.Client.Views;

namespace SecureExamIDE.Client;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = ClientServices.Build();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>()
            };

            desktop.ShutdownRequested += (_, _) => _services.Dispose();

            // The startup page decides where the user belongs, from whatever this computer has stored.
            StartupViewModel startup = _services.GetRequiredService<INavigationService>().NavigateTo<StartupViewModel>();
            startup.StartCommand.Execute(null);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
