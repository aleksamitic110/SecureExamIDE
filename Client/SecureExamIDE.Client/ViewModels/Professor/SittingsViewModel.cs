using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.ViewModels.Professor;

// The professor's sittings. The same screen serves two entrances: one exam's sittings, reached from
// the exam itself and able to schedule a new one, and all of them, reached from the home screen.
// Both read the same endpoint, which is what also shows cancelled sittings - the student's view of
// an exam shows only the active ones.
public sealed partial class SittingsViewModel(
    ISessionService session,
    INavigationService navigation,
    IProfessorExams exams,
    TimeProvider timeProvider) : SignedInViewModelBase(session, navigation), ILoadablePage
{
    private Guid? _examId;
    private string? _examTitle;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    private string? _confirmQuestion;

    private Func<Task>? _confirmedAction;

    // The scheduling form. Dates and times are the computer's local ones; the API client is the
    // single place they become UTC.
    [ObservableProperty]
    private bool _isScheduling;

    [ObservableProperty]
    private DateTimeOffset? _startDate;

    [ObservableProperty]
    private TimeSpan _startTime;

    [ObservableProperty]
    private DateTimeOffset? _endDate;

    [ObservableProperty]
    private TimeSpan _endTime;

    [ObservableProperty]
    private string? _scheduleError;

    [ObservableProperty]
    private bool _isSaving;

    // What the professor reads out. It exists here for as long as this screen is open and is in no
    // reply, database row or log anywhere afterwards.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewCode))]
    private string? _newCode;

    [ObservableProperty]
    private string _newCodeDetails = string.Empty;

    public ObservableCollection<MySittingItem> Sittings { get; } = [];

    // canSchedule is false for an exam that is not published: the API seals the package from the
    // finished set of files, so it refuses a sitting for a draft.
    public void Initialize(Guid? examId = null, string? examTitle = null, bool canSchedule = false)
    {
        _examId = examId;
        _examTitle = examTitle;
        CanSchedule = canSchedule;
    }

    public bool CanSchedule { get; private set; }

    public string Heading => _examTitle is null ? "All your sittings" : $"Sittings of {_examTitle}";

    public string Introduction => CanSchedule
        ? "Scheduling a sitting seals this exam's task files into a package for that date and gives you the one-time code that opens it. Each sitting gets its own code."
        : "A sitting can only be scheduled for a published exam, from that exam's own screen.";

    public bool IsEmpty => !IsLoading && ErrorMessage is null && Sittings.Count == 0;

    public string PageLabel => $"Page {Page} of {PageCount}";

    public bool IsConfirming => ConfirmQuestion is not null;

    public bool HasNewCode => NewCode is not null;

    [RelayCommand]
    private Task LoadAsync()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Introduction));
        OnPropertyChanged(nameof(CanSchedule));

        return LoadPageAsync(Page);
    }

    [RelayCommand(CanExecute = nameof(HasNextPage))]
    private Task NextPageAsync() => LoadPageAsync(Page + 1);

    [RelayCommand(CanExecute = nameof(HasPreviousPage))]
    private Task PreviousPageAsync() => LoadPageAsync(Page - 1);

    [RelayCommand]
    private void BeginSchedule()
    {
        // The next whole hour, for two hours: the common case, and still editable.
        DateTimeOffset start = timeProvider.GetLocalNow();
        start = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, 0, 0, start.Offset).AddHours(1);

        StartDate = start;
        StartTime = start.TimeOfDay;
        EndDate = start.AddHours(2);
        EndTime = start.AddHours(2).TimeOfDay;
        ScheduleError = null;
        IsScheduling = true;
    }

    [RelayCommand]
    private void CancelSchedule() => IsScheduling = false;

    [RelayCommand]
    private async Task ScheduleAsync()
    {
        if (_examId is not { } examId)
        {
            return;
        }

        DateTimeOffset? start = Combine(StartDate, StartTime);
        DateTimeOffset? end = Combine(EndDate, EndTime);

        if (start is null || end is null)
        {
            ScheduleError = "Choose a start and an end.";
            return;
        }

        // The same two rules the API applies, so an impossible sitting is not sent at all.
        if (end <= start)
        {
            ScheduleError = "The sitting has to end after it starts.";
            return;
        }

        if (end <= timeProvider.GetLocalNow())
        {
            ScheduleError = "The sitting has to end in the future.";
            return;
        }

        IsSaving = true;
        ScheduleError = null;

        try
        {
            ApiResult<ScheduledSitting> result = await exams.ScheduleSittingAsync(examId, start.Value, end.Value);

            if (!result.IsSuccess)
            {
                ScheduleError = result.Error.Message;
                return;
            }

            IsScheduling = false;
            NewCode = Format(result.Value.OneTimeCode);
            NewCodeDetails =
                $"{result.Value.StartsAt.ToLocalTime():ddd d MMM yyyy HH:mm} – {result.Value.EndsAt.ToLocalTime():HH:mm}";
        }
        finally
        {
            IsSaving = false;
        }

        await LoadPageAsync(1);
    }

    [RelayCommand]
    private void DismissCode()
    {
        NewCode = null;
        NewCodeDetails = string.Empty;
    }

    [RelayCommand]
    private void AskToCancelSitting(MySittingItem item)
    {
        ConfirmQuestion =
            $"Cancel the sitting on {item.When}? Students will no longer be able to open its package or hand in, " +
            "and it cannot be un-cancelled. Work already handed in stays.";

        _confirmedAction = async () =>
        {
            IsLoading = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                ApiResult result = await exams.CancelSittingAsync(item.Id);

                if (!result.IsSuccess)
                {
                    ErrorMessage = result.Error.Message;
                    return;
                }

                StatusMessage = "The sitting was cancelled.";
            }
            finally
            {
                IsLoading = false;
            }

            await LoadPageAsync(Page);
        };
    }

    [RelayCommand]
    private void CancelConfirm()
    {
        ConfirmQuestion = null;
        _confirmedAction = null;
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        Func<Task>? action = _confirmedAction;

        ConfirmQuestion = null;
        _confirmedAction = null;

        if (action is not null)
        {
            await action();
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (_examId is { } examId)
        {
            Navigation.NavigateTo<ExamContentsViewModel>(page => page.Initialize(examId));
        }
        else
        {
            Navigation.NavigateTo<ProfessorHomeViewModel>();
        }
    }

    [RelayCommand]
    private async Task LoadPageAsync(int page)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ApiResult<PagedList<MySitting>> result = await exams.GetMySittingsAsync(page, _examId);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();

            Sittings.Clear();

            foreach (MySitting sitting in result.Value.Items)
            {
                Sittings.Add(new MySittingItem(sitting, now));
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

    private static DateTimeOffset? Combine(DateTimeOffset? date, TimeSpan time) =>
        date is null
            ? null
            : new DateTimeOffset(date.Value.Date, date.Value.Offset).Add(time);

    // Shown in the groups the provider generates them in, which is how they are meant to be read out.
    private static string Format(string code) =>
        code.Contains('-', StringComparison.Ordinal)
            ? code
            : string.Join('-', Enumerable.Range(0, (code.Length + 3) / 4)
                .Select(i => code.Substring(i * 4, Math.Min(4, code.Length - (i * 4)))))
                .ToUpper(CultureInfo.InvariantCulture);
}
