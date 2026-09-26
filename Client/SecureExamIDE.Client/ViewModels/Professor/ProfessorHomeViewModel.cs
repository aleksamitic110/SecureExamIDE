using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.ViewModels.Professor;

// The professor's starting point: their own exams, a page at a time, drafts included. The student
// catalog cannot serve this - it shows published exams only, so until this screen existed a draft
// could only be reached through an id the professor had written down somewhere.
public sealed partial class ProfessorHomeViewModel(
    ISessionService session,
    INavigationService navigation,
    IProfessorExams exams) : SignedInViewModelBase(session, navigation), ILoadablePage
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

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

    // Which exam the delete confirmation is asking about. Throwing a draft away also destroys its
    // task files and toolchains in storage, so it is never one click.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingDelete), nameof(DeleteQuestion))]
    private MyExamItem? _pendingDelete;

    [ObservableProperty]
    private ExamStatusOption _selectedStatus = ExamStatusOption.All;

    public ObservableCollection<MyExamItem> Exams { get; } = [];

    public IReadOnlyList<ExamStatusOption> StatusOptions => ExamStatusOption.Options;

    public string Details => Session.CurrentUser is { } user
        ? $"Professor · {user.Email}"
        : string.Empty;

    public bool IsEmpty => !IsLoading && ErrorMessage is null && Exams.Count == 0;

    public string PageLabel => $"Page {Page} of {PageCount}";

    public bool IsConfirmingDelete => PendingDelete is not null;

    public string DeleteQuestion => PendingDelete is { } item
        ? $"Delete the draft \"{item.Title}\"? Its task files and toolchains are deleted with it, and this cannot be undone."
        : string.Empty;

    [RelayCommand]
    private Task LoadAsync() => LoadPageAsync(Page);

    [RelayCommand(CanExecute = nameof(HasNextPage))]
    private Task NextPageAsync() => LoadPageAsync(Page + 1);

    [RelayCommand(CanExecute = nameof(HasPreviousPage))]
    private Task PreviousPageAsync() => LoadPageAsync(Page - 1);

    [RelayCommand]
    private void NewExam() =>
        Navigation.NavigateTo<ExamEditorViewModel>(page => page.Initialize(null));

    [RelayCommand]
    private void OpenAllSittings() => Navigation.NavigateTo<SittingsViewModel>(page => page.Initialize());

    // Task files, toolchains and publishing live on the exam's own screen.
    [RelayCommand]
    private void OpenExam(MyExamItem item) =>
        Navigation.NavigateTo<ExamContentsViewModel>(page => page.Initialize(item.Id));

    [RelayCommand]
    private void EditExam(MyExamItem item) =>
        Navigation.NavigateTo<ExamEditorViewModel>(page => page.Initialize(item.Id));

    [RelayCommand]
    private void AskToDelete(MyExamItem item) => PendingDelete = item;

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (PendingDelete is not { } item)
        {
            return;
        }

        PendingDelete = null;
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            ApiResult result = await exams.DeleteExamAsync(item.Id);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            StatusMessage = $"\"{item.Title}\" was deleted.";
        }
        finally
        {
            IsLoading = false;
        }

        // Read the page back rather than removing the row here: the page may now be short, or empty
        // altogether, and the server decides that.
        await LoadPageAsync(Page);
    }

    // Changing the filter starts again at the first page, because page 3 of the drafts has nothing
    // to do with page 3 of everything.
    partial void OnSelectedStatusChanged(ExamStatusOption value) => LoadPageCommand.Execute(1);

    [RelayCommand]
    private async Task LoadPageAsync(int page)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ApiResult<PagedList<MyExam>> result = await exams.GetMyExamsAsync(page, SelectedStatus.Value);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            Exams.Clear();

            foreach (MyExam exam in result.Value.Items)
            {
                Exams.Add(new MyExamItem(exam));
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
