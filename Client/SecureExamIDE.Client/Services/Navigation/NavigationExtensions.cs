using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.Services.Navigation;

public static class NavigationExtensions
{
    // Students and professors use the same application but land on different screens; the role on
    // the signed-in profile decides which.
    public static void NavigateToHome(this INavigationService navigation, UserProfile user)
    {
        if (user.Role == UserRole.Professor)
        {
            navigation.NavigateTo<ProfessorHomeViewModel>();
        }
        else
        {
            navigation.NavigateTo<StudentHomeViewModel>();
        }
    }
}
