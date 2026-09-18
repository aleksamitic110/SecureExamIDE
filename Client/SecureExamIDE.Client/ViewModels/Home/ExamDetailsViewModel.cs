using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.ExamDay;

namespace SecureExamIDE.Client.ViewModels.Home;

// One exam: its sittings and the toolchains it needs, and the download that prepares this
// computer for a sitting. The download is meant to happen at home, days before the exam.
public sealed partial class ExamDetailsViewModel(
    ISessionService session,
    INavigationService navigation,
    IExamCatalog catalog,
    IExamDownloadService downloads,
    ILocalExamLibrary library,
    TimeProvider timeProvider) : SignedInViewModelBase(session, navigation), ILoadablePage, IDisposable
{
    private CatalogExam? _exam;
    private CancellationTokenSource? _download;
    private IReadOnlyList<ExamDependency> _dependencies = [];

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSittings))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(CancelDownloadCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    public ObservableCollection<SittingItemViewModel> Sittings { get; } = [];

    public ObservableCollection<DependencyItem> Dependencies { get; } = [];

    public bool HasNoSittings => !IsLoading && Sittings.Count == 0;

    public bool HasNoDependencies => Dependencies.Count == 0;

    public void Initialize(CatalogExam exam)
    {
        _exam = exam;
        Title = exam.Title;
        Subtitle = $"{exam.Subject} · {exam.ProfessorFirstName} {exam.ProfessorLastName}";
        Description = exam.Description;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_exam is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ApiResult<IReadOnlyList<ExamSitting>> sittings = await catalog.GetSittingsAsync(_exam.Id);
            ApiResult<IReadOnlyList<ExamDependency>> dependencies = sittings.IsSuccess
                ? await catalog.GetDependenciesAsync(_exam.Id)
                : ApiResult.Failure<IReadOnlyList<ExamDependency>>(sittings.Error);

            if (!dependencies.IsSuccess)
            {
                ErrorMessage = dependencies.Error.Message;
                return;
            }

            DownloadedExam? local = await library.LoadAsync(_exam.Id);

            Sittings.Clear();

            foreach (ExamSitting sitting in sittings.Value)
            {
                Sittings.Add(new SittingItemViewModel(sitting, IsDownloaded(local, sitting), timeProvider));
            }

            _dependencies = dependencies.Value;
            ShowDependencies(local);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoSittings));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync(SittingItemViewModel item)
    {
        if (_exam is null)
        {
            return;
        }

        _download = new CancellationTokenSource();
        IsDownloading = true;
        ErrorMessage = null;
        StatusMessage = null;
        ProgressPercent = 0;
        ProgressText = "Preparing the download…";

        // Created here so progress reports arrive on the UI thread.
        var progress = new Progress<ExamDownloadProgress>(ShowProgress);

        try
        {
            ApiResult result = await downloads.DownloadSittingAsync(_exam, item.Sitting, progress, _download.Token);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Code == ErrorCodes.SittingCancelled
                    ? "This sitting has been cancelled by the professor."
                    : result.Error.Message;
                return;
            }

            item.IsDownloaded = true;
            ShowDependencies(await library.LoadAsync(_exam.Id));
            StatusMessage = "Everything needed for this sitting is now on this computer.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download stopped. Starting it again continues where it stopped.";
        }
        finally
        {
            IsDownloading = false;
            _download.Dispose();
            _download = null;
        }
    }

    private bool CanDownload(SittingItemViewModel? item) => !IsDownloading && item is { CanDownload: true };

    [RelayCommand(CanExecute = nameof(IsDownloading))]
    private void CancelDownload() => _download?.Cancel();

    // Straight into the exam from the catalog: the code screen, and then the workspace. Everything it
    // needs comes from the local record, so it works exactly as it does offline.
    [RelayCommand]
    private async Task EnterAsync(SittingItemViewModel item)
    {
        if (_exam is null)
        {
            return;
        }

        DownloadedExam? local = await library.LoadAsync(_exam.Id);
        DownloadedSitting? sitting = local?.Sittings.FirstOrDefault(s => s.SittingId == item.Sitting.Id);

        if (local is null || sitting is null)
        {
            // The record is gone or was never written; downloading it again is the way back.
            item.IsDownloaded = false;
            ErrorMessage = "This sitting is no longer on this computer. Download it again.";

            return;
        }

        Navigation.NavigateTo<UnlockSittingViewModel>(page => page.Initialize(local, sitting));
    }

    [RelayCommand]
    private void Back() => Navigation.NavigateTo<StudentHomeViewModel>();

    // Leaving the screen stops a download in progress; what arrived is kept for the next attempt.
    public void Dispose() => _download?.Cancel();

    private void ShowProgress(ExamDownloadProgress progress)
    {
        ProgressPercent = progress.BytesTotal > 0 ? progress.BytesDone * 100d / progress.BytesTotal : 0;
        ProgressText = $"Downloading {progress.CurrentItem} ({progress.ItemNumber} of {progress.ItemCount}) · " +
                       $"{ByteSize.Format(progress.BytesDone)} of {ByteSize.Format(progress.BytesTotal)}";
    }

    private void ShowDependencies(DownloadedExam? local)
    {
        HashSet<Guid> onDisk = [.. (local?.Dependencies ?? []).Select(d => d.DependencyId)];

        Dependencies.Clear();

        foreach (ExamDependency dependency in _dependencies)
        {
            Dependencies.Add(new DependencyItem(dependency, onDisk.Contains(dependency.Id)));
        }

        OnPropertyChanged(nameof(HasNoDependencies));
    }

    private static bool IsDownloaded(DownloadedExam? local, ExamSitting sitting) =>
        local?.Sittings.Any(s => s.SittingId == sitting.Id && s.PackageSha256 == sitting.PackageSha256) == true;
}
