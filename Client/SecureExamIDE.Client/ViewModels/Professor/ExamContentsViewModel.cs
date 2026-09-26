using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Files;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One exam's contents: its task files, the toolchains students will need, and publishing. This is
// the screen that makes an exam usable - until it has at least one task file it cannot be published,
// and until it is published no student can see it.
public sealed partial class ExamContentsViewModel(
    ISessionService session,
    INavigationService navigation,
    IProfessorExams exams,
    IExamContentService content,
    IFilePicker filePicker) : SignedInViewModelBase(session, navigation), ILoadablePage, IDisposable
{
    private Guid _examId;
    private CancellationTokenSource? _upload;
    private Func<Task>? _confirmedAction;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraft))]
    [NotifyCanExecuteChangedFor(nameof(AddTaskFileCommand), nameof(BeginAddToolchainCommand), nameof(AskToPublishCommand))]
    private ExamStatus _status = ExamStatus.Draft;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskFileCommand), nameof(BeginAddToolchainCommand), nameof(AskToPublishCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(AddTaskFileCommand),
        nameof(BeginAddToolchainCommand),
        nameof(AskToPublishCommand),
        nameof(AddToolchainCommand),
        nameof(CancelUploadCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    // The toolchain form. A compiler needs a name, a version and the platform it runs on before the
    // archive means anything.
    [ObservableProperty]
    private bool _isAddingToolchain;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToolchainCommand))]
    private string _toolchainName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToolchainCommand))]
    private string _toolchainVersion = string.Empty;

    [ObservableProperty]
    private PlatformOption _selectedPlatform = PlatformOption.Options[0];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToolchainCommand))]
    private string? _toolchainFileLabel;

    private string? _toolchainFilePath;

    // One confirmation overlay serves publishing and both kinds of removal: each of them destroys
    // something that cannot be got back by pressing the button again.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    private string? _confirmQuestion;

    public ObservableCollection<ExamFileItem> Files { get; } = [];

    public ObservableCollection<ExamDependencyItem> Dependencies { get; } = [];

    public IReadOnlyList<PlatformOption> PlatformOptions => PlatformOption.Options;

    public void Initialize(Guid examId) => _examId = examId;

    public bool IsDraft => Status == ExamStatus.Draft;

    public bool IsConfirming => ConfirmQuestion is not null;

    public bool HasNoFiles => Files.Count == 0;

    public string PublishNote => Files.Count == 0
        ? "An exam needs at least one task file before it can be published."
        : "Publishing puts the exam in the students' catalog. After that nothing about it can be changed.";

    public int NameMaxLength => ExamLimits.DependencyNameMaxLength;

    public int VersionMaxLength => ExamLimits.DependencyVersionMaxLength;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ApiResult<ExamDetails> result = await exams.GetExamAsync(_examId);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            ExamDetails exam = result.Value;

            Title = exam.Title;
            Subtitle = $"{exam.Subject} · {exam.Status}";
            Status = exam.Status;

            Files.Clear();

            foreach (ExamFileInfo file in exam.Files)
            {
                Files.Add(new ExamFileItem(file));
            }

            Dependencies.Clear();

            foreach (ExamDependency dependency in exam.Dependencies)
            {
                Dependencies.Add(new ExamDependencyItem(dependency));
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(PublishNote));

            // Whether publishing is possible depends on the file count, which a collection change
            // does not announce on its own.
            AskToPublishCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task AddTaskFileAsync()
    {
        PickedFile? picked = await filePicker.PickFileAsync("Choose a task file");

        if (picked is null)
        {
            return;
        }

        await RunAsync(
            $"Uploading {picked.Name}…",
            token => content.AddTaskFileAsync(_examId, picked.Path, picked.Name, token),
            $"\"{picked.Name}\" was added.");
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void BeginAddToolchain()
    {
        ToolchainName = string.Empty;
        ToolchainVersion = string.Empty;
        ToolchainFileLabel = null;
        _toolchainFilePath = null;
        SelectedPlatform = PlatformOption.Options[0];
        IsAddingToolchain = true;
    }

    [RelayCommand]
    private void CancelAddToolchain() => IsAddingToolchain = false;

    [RelayCommand]
    private async Task ChooseToolchainFileAsync()
    {
        PickedFile? picked = await filePicker.PickFileAsync("Choose a toolchain archive");

        if (picked is null)
        {
            return;
        }

        _toolchainFilePath = picked.Path;
        ToolchainFileLabel = $"{picked.Name} ({ByteSize.Format(picked.SizeBytes)})";
    }

    [RelayCommand(CanExecute = nameof(CanAddToolchain))]
    private async Task AddToolchainAsync()
    {
        string path = _toolchainFilePath!;
        string name = ToolchainName.Trim();
        string version = ToolchainVersion.Trim();
        DependencyPlatform platform = SelectedPlatform.Value;

        IsAddingToolchain = false;

        long total = new FileInfo(path).Length;
        var progress = new Progress<long>(sent =>
        {
            ProgressPercent = total == 0 ? 0 : sent * 100.0 / total;
            ProgressText = $"Uploading {name} {version}… {ByteSize.Format(sent)} of {ByteSize.Format(total)}";
        });

        await RunAsync(
            $"Uploading {name} {version}…",
            token => content.AddToolchainAsync(_examId, path, name, version, platform, progress, token),
            $"{name} {version} was added.");
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelUpload() => _upload?.Cancel();

    [RelayCommand]
    private void AskToRemoveFile(ExamFileItem item) =>
        Ask(
            $"Remove the task file \"{item.FileName}\"? It is deleted from the server as well, and this cannot be undone.",
            token => exams.RemoveFileAsync(_examId, item.Id, token),
            $"\"{item.FileName}\" was removed.");

    [RelayCommand]
    private void AskToRemoveDependency(ExamDependencyItem item) =>
        Ask(
            $"Remove the toolchain \"{item.Name}\"? It is deleted from the server as well, and this cannot be undone.",
            token => exams.RemoveDependencyAsync(_examId, item.Id, token),
            $"{item.Name} was removed.");

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private void AskToPublish() =>
        Ask(
            $"Publish \"{Title}\"? Students will be able to download it, and nothing about the exam can be changed afterwards.",
            token => exams.PublishExamAsync(_examId, token),
            "The exam was published.");

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
    private void EditDetails() =>
        Navigation.NavigateTo<ExamEditorViewModel>(page => page.Initialize(_examId));

    [RelayCommand]
    private void Back() => Navigation.NavigateTo<ProfessorHomeViewModel>();

    public void Dispose()
    {
        _upload?.Cancel();
        _upload?.Dispose();
        _upload = null;
    }

    private void Ask(string question, Func<CancellationToken, Task<ApiResult>> action, string done)
    {
        ConfirmQuestion = question;
        _confirmedAction = () => RunAsync(null, action, done);
    }

    // Every change follows the same shape: say what is happening, do it, then read the exam back
    // from the server rather than guessing what it now looks like.
    private async Task RunAsync(string? busyText, Func<CancellationToken, Task<ApiResult>> action, string done)
    {
        _upload?.Dispose();
        _upload = new CancellationTokenSource();

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        ProgressPercent = 0;
        ProgressText = busyText ?? string.Empty;

        try
        {
            ApiResult result = await action(_upload.Token);

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            StatusMessage = done;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "The upload was stopped. Nothing was added.";
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            ProgressPercent = 0;
        }

        await LoadAsync();
    }

    private bool CanChange() => IsDraft && !IsBusy && !IsLoading;

    private bool CanPublish() => CanChange() && Files.Count > 0;

    private bool CanAddToolchain() =>
        !IsBusy &&
        _toolchainFilePath is not null &&
        ExamLimits.IsWithin(ToolchainName, ExamLimits.DependencyNameMaxLength) &&
        ExamLimits.IsWithin(ToolchainVersion, ExamLimits.DependencyVersionMaxLength);
}
