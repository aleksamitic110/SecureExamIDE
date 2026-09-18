using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using Microsoft.Extensions.Options;
using SecureExamIDE.Client.Services.Lockdown;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.Services.Workspace;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

// Takes the one-time code the professor gives out when the sitting starts and opens the package
// with it, on this computer, with no network. The code is the lock; the clock is only shown, never
// enforced, so a laptop with a wrong clock cannot shut a student out of their own exam.
//
// A sitting the student has finished stays closed: the code no longer opens it from here. A testing
// build - the same switch that provides the emergency exit - opens it again, and says so, because
// otherwise every test of the exam costs a fresh sitting.
public sealed partial class UnlockSittingViewModel(
    INavigationService navigation,
    IPackageUnlocker unlocker,
    ILocalExamLibrary library,
    IWorkspaceStore workspace,
    IOptions<LockdownOptions> lockdownOptions,
    TimeProvider timeProvider) : ViewModelBase
{
    private DownloadedExam? _exam;
    private DownloadedSitting? _sitting;

    [ObservableProperty]
    private string _examTitle = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isUnlocking;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    [NotifyPropertyChangedFor(nameof(CanEnterCode))]
    private bool _isFinished;

    public bool CanEnterCode => !IsFinished || IsTestingBuild;

    private bool IsTestingBuild => lockdownOptions.Value.AllowEmergencyExit;

    public void Initialize(DownloadedExam exam, DownloadedSitting sitting)
    {
        _exam = exam;
        _sitting = sitting;

        var item = new LocalSittingItem(exam, sitting, timeProvider);
        ExamTitle = exam.Title;
        Details = $"{item.Subject} · {item.When}";
        IsFinished = workspace.IsFinished(exam.ExamId, sitting.SittingId);
        Status = IsFinished switch
        {
            true when IsTestingBuild => "You finished this sitting. Testing build: the code opens it again anyway.",
            true => "You have finished this sitting. It cannot be opened again.",
            _ => item.Status switch
            {
                "Upcoming" => "This sitting has not started yet. The professor gives out the code when it starts.",
                "Ended" => "This sitting has ended.",
                _ => "Enter the code the professor gave out for this sitting."
            }
        };
    }

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        if (_exam is null || _sitting is null || !CanEnterCode)
        {
            return;
        }

        ErrorMessage = null;
        IsUnlocking = true;

        try
        {
            ApiResult<UnlockedExam> result = await unlocker.UnlockAsync(
                library.PackagePath(_exam.ExamId, _sitting.SittingId),
                library.HeaderPath(_exam.ExamId, _sitting.SittingId),
                _sitting.PackageSha256,
                Code);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            // The typed code is not kept once it has done its job.
            Code = string.Empty;

            UnlockedExam unlocked = result.Value;
            navigation.NavigateTo<WorkspaceViewModel>(page => page.Initialize(_exam, _sitting, unlocked));
        }
        finally
        {
            IsUnlocking = false;
        }
    }

    private bool CanUnlock() => !IsUnlocking && CanEnterCode;

    [RelayCommand]
    private void Back() => navigation.NavigateTo<DownloadedExamsViewModel>();
}
