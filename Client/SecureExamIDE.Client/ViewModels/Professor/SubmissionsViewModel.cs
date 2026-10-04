using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Review;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.ViewModels.Professor;

// Who handed in for one sitting, and the way into what they handed in.
//
// The sitting's one-time code is asked for **once**, when the first solution is opened. The key it
// derives is held by IReviewKeyCache - in memory, for one sitting, forgotten on leaving the sitting or
// after half an hour of nothing - so marking thirty students costs one typing of the code while an
// unattended computer does not keep a class's work unlocked.
public sealed partial class SubmissionsViewModel(
    ISessionService session,
    INavigationService navigation,
    ISubmissionReview review,
    IReviewKeyCache keys) : SignedInViewModelBase(session, navigation), ILoadablePage
{
    private Guid _sittingId;
    private string _examTitle = string.Empty;
    private string _when = string.Empty;
    private SubmissionItem? _waitingToOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingForCode))]
    private bool _isCodeNeeded;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string? _codeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingForCode))]
    private bool _isOpening;

    public ObservableCollection<SubmissionItem> Submissions { get; } = [];

    public void Initialize(Guid sittingId, string examTitle, string when)
    {
        _sittingId = sittingId;
        _examTitle = examTitle;
        _when = when;
    }

    public string Heading => $"Handed in for {_examTitle}";

    public string Details => _when;

    public bool IsEmpty => !IsLoading && ErrorMessage is null && Submissions.Count == 0;

    public bool IsAskingForCode => IsCodeNeeded && !IsOpening;

    // Says whether the code has already been given, so the screen can explain what Open will do.
    public bool IsSittingOpen => keys.Get(_sittingId) is not null;

    [RelayCommand]
    private async Task LoadAsync()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Details));

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ApiResult<PagedList<SubmissionSummary>> result = await review.GetSubmissionsAsync(_sittingId, page: 1);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;

                return;
            }

            Submissions.Clear();

            foreach (SubmissionSummary submission in result.Value.Items)
            {
                Submissions.Add(new SubmissionItem(submission));
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    // The first Open asks for the code; every one after it opens straight away.
    [RelayCommand]
    private async Task OpenAsync(SubmissionItem item)
    {
        if (keys.Get(_sittingId) is null)
        {
            _waitingToOpen = item;
            Code = string.Empty;
            CodeError = null;
            IsCodeNeeded = true;

            return;
        }

        await OpenWithKeyAsync(item);
    }

    [RelayCommand]
    private void CancelCode()
    {
        IsCodeNeeded = false;
        _waitingToOpen = null;
    }

    [RelayCommand]
    private async Task ConfirmCodeAsync()
    {
        IsOpening = true;
        CodeError = null;

        try
        {
            // Argon2id at the exam's own parameters - about half a second, and only this once.
            ApiResult<byte[]> key = await review.DeriveHandInKeyAsync(_sittingId, Code);

            if (!key.IsSuccess)
            {
                CodeError = key.Error.Message;

                return;
            }

            keys.Set(_sittingId, key.Value);
            Code = string.Empty;
            IsCodeNeeded = false;
            OnPropertyChanged(nameof(IsSittingOpen));

            if (_waitingToOpen is { } waiting)
            {
                _waitingToOpen = null;
                await OpenWithKeyAsync(waiting);
            }
        }
        finally
        {
            IsOpening = false;
        }
    }

    private async Task OpenWithKeyAsync(SubmissionItem item)
    {
        IsOpening = true;
        ErrorMessage = null;

        try
        {
            if (keys.Get(_sittingId) is not { } handInKey)
            {
                // Forgotten while the screen sat open; the code is asked for again.
                await OpenAsync(item);

                return;
            }

            ApiResult<OpenedSubmission> opened = await review.OpenAsync(_sittingId, item.Submission, handInKey);

            if (!opened.IsSuccess)
            {
                ErrorMessage = opened.Error.Message;

                return;
            }

            Navigation.NavigateTo<SubmissionReviewViewModel>(page => page.Initialize(
                _sittingId, _examTitle, item, opened.Value));
        }
        finally
        {
            IsOpening = false;
        }
    }

    // Going back to the sittings list leaves this sitting, so the code is forgotten.
    [RelayCommand]
    private void Back()
    {
        keys.Clear();
        Navigation.NavigateTo<SittingsViewModel>(page => page.Initialize());
    }
}
