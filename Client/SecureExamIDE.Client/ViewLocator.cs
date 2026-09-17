using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SecureExamIDE.Client.ViewModels;
using SecureExamIDE.Client.ViewModels.Account;
using SecureExamIDE.Client.ViewModels.ExamDay;
using SecureExamIDE.Client.ViewModels.Home;
using SecureExamIDE.Client.Views.Account;
using SecureExamIDE.Client.Views.ExamDay;
using SecureExamIDE.Client.Views.Home;

namespace SecureExamIDE.Client;

// Picks the view for a page's view model. An explicit table rather than the template's reflection
// on type names: a missing entry is visible here, and nothing depends on names lining up or on
// types surviving trimming.
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Views = new()
    {
        [typeof(StartupViewModel)] = () => new StartupView(),
        [typeof(WelcomeViewModel)] = () => new WelcomeView(),
        [typeof(RegisterViewModel)] = () => new RegisterView(),
        [typeof(VerifyEmailViewModel)] = () => new VerifyEmailView(),
        [typeof(LoginViewModel)] = () => new LoginView(),
        [typeof(StudentHomeViewModel)] = () => new StudentHomeView(),
        [typeof(ExamDetailsViewModel)] = () => new ExamDetailsView(),
        [typeof(DownloadedExamsViewModel)] = () => new DownloadedExamsView(),
        [typeof(UnlockSittingViewModel)] = () => new UnlockSittingView(),
        [typeof(ExamTasksViewModel)] = () => new ExamTasksView(),
        [typeof(ProfessorHomeViewModel)] = () => new ProfessorHomeView()
    };

    public Control? Build(object? param) =>
        param is not null && Views.TryGetValue(param.GetType(), out Func<Control>? create)
            ? create()
            : new TextBlock { Text = $"No view is registered for {param?.GetType().Name}." };

    public bool Match(object? data) => data is ViewModelBase;
}
