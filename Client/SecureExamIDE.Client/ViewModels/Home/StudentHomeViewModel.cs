using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.ExamDay;

namespace SecureExamIDE.Client.ViewModels.Home;

// The student's starting point: the published exams, a page at a time, marked where something has
// already been downloaded to this computer.
public sealed partial class StudentHomeViewModel(
    ISessionService session,
    INavigationService navigation,
    IExamCatalog catalog,
    ILocalExamLibrary library) : SignedInViewModelBase(session, navigation), ILoadablePage
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    private int _pageCount = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private bool _hasNextPage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private bool _hasPreviousPage;

    public ObservableCollection<CatalogExamItem> Exams { get; } = [];

    public string Details => Session.CurrentUser is { } user
        ? $"Student · {user.Email} · Index number {user.IndexNumber}"
        : string.Empty;

    public bool IsEmpty => !IsLoading && ErrorMessage is null && Exams.Count == 0;

    public string PageLabel => $"Page {Page} of {PageCount}";

    [RelayCommand]
    private Task LoadAsync() => LoadPageAsync(Page);

    [RelayCommand(CanExecute = nameof(HasNextPage))]
    private Task NextPageAsync() => LoadPageAsync(Page + 1);

    [RelayCommand(CanExecute = nameof(HasPreviousPage))]
    private Task PreviousPageAsync() => LoadPageAsync(Page - 1);

    [RelayCommand]
    private void OpenDownloadedExams() => Navigation.NavigateTo<DownloadedExamsViewModel>();

    [RelayCommand]
    private void OpenExam(CatalogExamItem item) =>
        Navigation.NavigateTo<ExamDetailsViewModel>(page => page.Initialize(item.Exam));

    private async Task LoadPageAsync(int page)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ApiResult<PagedList<CatalogExam>> result = await catalog.GetPublishedExamsAsync(page);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            HashSet<Guid> downloaded = [.. (await library.LoadAllAsync())
                .Where(e => e.Sittings.Count > 0)
                .Select(e => e.ExamId)];

            Exams.Clear();

            foreach (CatalogExam exam in result.Value.Items)
            {
                Exams.Add(new CatalogExamItem(exam, downloaded.Contains(exam.Id)));
            }

            Page = result.Value.Page;
            PageCount = Math.Max(1, (int)Math.Ceiling(result.Value.TotalCount / (double)Math.Max(1, result.Value.PageSize)));
            HasNextPage = result.Value.HasNextPage;
            HasPreviousPage = result.Value.HasPreviousPage;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
