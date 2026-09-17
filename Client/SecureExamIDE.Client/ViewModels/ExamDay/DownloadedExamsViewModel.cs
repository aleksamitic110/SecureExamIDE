using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Account;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

// The exam-day starting point: every sitting downloaded to this computer, read from disk alone, so it
// works the same with or without a connection. Sittings in progress come first.
public sealed partial class DownloadedExamsViewModel(
    ISessionService session,
    INavigationService navigation,
    ILocalExamLibrary library,
    TimeProvider timeProvider) : SignedInViewModelBase(session, navigation), ILoadablePage
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    public ObservableCollection<LocalSittingItem> Sittings { get; } = [];

    public bool IsEmpty => !IsLoading && Sittings.Count == 0;

    public string ConnectionNote => Session.IsOffline
        ? "Working offline. Only exams already downloaded to this computer are available."
        : "These are the sittings downloaded to this computer. They open without an internet connection.";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            IReadOnlyList<DownloadedExam> exams = await library.LoadAllAsync();

            Sittings.Clear();

            IEnumerable<LocalSittingItem> items = exams
                .SelectMany(exam => exam.Sittings.Select(sitting => new LocalSittingItem(exam, sitting, timeProvider)))
                .OrderByDescending(item => item.IsInProgress)
                .ThenBy(item => item.Status == "Ended")
                .ThenBy(item => item.Sitting.StartsAt);

            foreach (LocalSittingItem item in items)
            {
                Sittings.Add(item);
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private void Open(LocalSittingItem item) =>
        Navigation.NavigateTo<UnlockSittingViewModel>(page => page.Initialize(item.Exam, item.Sitting));

    [RelayCommand]
    private void BackToCatalog() => Navigation.NavigateTo<StudentHomeViewModel>();

    // Offline, the way back to the catalog is through the connection check at start-up.
    [RelayCommand]
    private void Reconnect() => Navigation.NavigateTo<StartupViewModel>(page => page.StartCommand.Execute(null));
}
