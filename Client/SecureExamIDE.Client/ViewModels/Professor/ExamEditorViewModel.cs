using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.ViewModels.Professor;

// Creates a new exam, or corrects a draft. One screen for both, because the fields are the same
// three either way; which it is depends only on whether Initialize was given an id.
public sealed partial class ExamEditorViewModel(
    ISessionService session,
    INavigationService navigation,
    IProfessorExams exams) : SignedInViewModelBase(session, navigation), ILoadablePage
{
    private Guid? _examId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _subject = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    private string? _errorMessage;

    // Set when an exam turns out not to be a draft any more. The fields then stay on screen so the
    // professor can read them, but nothing can be saved.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isReadOnly;

    // Null creates a new exam; an id corrects that one.
    public void Initialize(Guid? examId) => _examId = examId;

    public bool IsNew => _examId is null;

    public string Heading => IsNew ? "New exam" : "Edit exam";

    public string Introduction => IsNew
        ? "An exam starts as a draft. Task files and toolchains are added next, and only then can it be published."
        : "A draft can still be corrected. Once an exam is published it cannot be changed, because students may already have downloaded it.";

    public string SaveLabel => IsNew ? "Create exam" : "Save changes";

    public int TitleMaxLength => ExamLimits.TitleMaxLength;

    public int DescriptionMaxLength => ExamLimits.DescriptionMaxLength;

    public int SubjectMaxLength => ExamLimits.SubjectMaxLength;

    [RelayCommand]
    private async Task LoadAsync()
    {
        // Notify for the properties that only become meaningful once Initialize has run.
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Introduction));
        OnPropertyChanged(nameof(SaveLabel));

        if (_examId is not { } examId)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // The list leaves the description out, so the full exam is read before the form is filled.
            ApiResult<ExamDetails> result = await exams.GetExamAsync(examId);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                IsReadOnly = true;
                return;
            }

            Title = result.Value.Title;
            Description = result.Value.Description;
            Subject = result.Value.Subject;

            if (result.Value.Status != ExamStatus.Draft)
            {
                IsReadOnly = true;
                ErrorMessage = $"This exam is {Describe(result.Value.Status)} and can no longer be changed.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsSaving = true;
        ErrorMessage = null;

        try
        {
            ApiResult result = _examId is { } examId
                ? await exams.UpdateExamAsync(examId, Title.Trim(), Description.Trim(), Subject.Trim())
                : await exams.CreateExamAsync(Title.Trim(), Description.Trim(), Subject.Trim());

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }
        }
        finally
        {
            IsSaving = false;
        }

        Navigation.NavigateTo<ProfessorHomeViewModel>();
    }

    [RelayCommand]
    private void Cancel() => Navigation.NavigateTo<ProfessorHomeViewModel>();

    private static string Describe(ExamStatus status) => status switch
    {
        ExamStatus.Published => "published",
        ExamStatus.Archived => "archived",
        _ => "no longer a draft"
    };

    // The same lengths the API enforces, so the form does not send a request that cannot succeed.
    private bool CanSave() =>
        !IsSaving &&
        !IsLoading &&
        !IsReadOnly &&
        ExamLimits.IsWithin(Title, ExamLimits.TitleMaxLength) &&
        ExamLimits.IsWithin(Description, ExamLimits.DescriptionMaxLength) &&
        ExamLimits.IsWithin(Subject, ExamLimits.SubjectMaxLength);
}
