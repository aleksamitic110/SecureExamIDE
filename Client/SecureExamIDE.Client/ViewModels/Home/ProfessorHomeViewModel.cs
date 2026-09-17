using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.ViewModels.Home;

// The professor's starting point. Exam management arrives here once the student flow is complete.
public sealed class ProfessorHomeViewModel(ISessionService session, INavigationService navigation)
    : SignedInViewModelBase(session, navigation)
{
    public string Details => Session.CurrentUser is { } user
        ? $"Professor · {user.Email}"
        : string.Empty;
}
