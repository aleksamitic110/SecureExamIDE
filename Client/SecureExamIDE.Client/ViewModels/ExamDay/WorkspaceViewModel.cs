using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Lockdown;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Pdf;
using SecureExamIDE.Client.Services.Run;
using SecureExamIDE.Client.Services.Storage;
using SecureExamIDE.Client.Services.Submission;
using SecureExamIDE.Client.Services.Toolchains;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.Services.Workspace;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

// The exam itself: the student's files on the left, the editor in the middle, the unlocked tasks on
// the right. Opening it locks the application fullscreen, and the only way out is finishing the exam.
//
// Everything typed is saved on its own shortly after the typing stops, so a crash, a flat battery or
// a forced shutdown loses at most the last moment of work, and reopening the sitting with the code
// brings the files back. The files are encrypted on disk with the key the unlock derived, so the
// work can only be read back inside the exam.
public sealed partial class WorkspaceViewModel(
    INavigationService navigation,
    IWorkspaceStore store,
    IActivityLogStore activityLogs,
    ISubmissionService submissions,
    IToolchainService toolchains,
    IProgramRunner runner,
    IPdfRenderer pdfRenderer,
    IExamLockdown lockdown,
    IOptions<LockdownOptions> lockdownOptions,
    IUiPreferences preferences,
    TimeProvider timeProvider) : ViewModelBase, IDisposable
{
    private readonly StringBuilder _console = new();
    private readonly Lock _saveLock = new();
    private readonly Dictionary<WorkspaceFileItem, string> _unsaved = [];

    // Set once the work has been sealed: saving again would write the student's files back after the
    // seal was taken, which is exactly what finishing is supposed to prevent.
    private bool _unsavedCleared;

    private DownloadedExam? _exam;
    private DownloadedSitting? _sitting;
    private UnlockedExam? _unlocked;
    private IWorkspaceFiles? _files;
    private IActivityLog? _activity;
    private ITimer? _autosave;
    private SynchronizationContext? _uiContext;
    private IReadOnlyList<Toolchain>? _toolchains;
    private ITimer? _clock;
    private DateTimeOffset _endsAt;
    private int _lastWarningMinutes;
    private CancellationTokenSource? _running;
    private Channel<string>? _input;

    [ObservableProperty]
    private string _examTitle = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFile), nameof(HasNoActiveFile))]
    private WorkspaceFileItem? _activeFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTaskText), nameof(IsSelectedTaskTextShown), nameof(IsSelectedTaskBinary), nameof(IsSelectedTaskPdf))]
    private ExamTaskItem? _selectedTask;

    // How long is left of the sitting. Shown, never enforced: what happens at the end belongs to
    // handing in, and a laptop with a wrong clock must not shut a student out of their own exam.
    [ObservableProperty]
    private string _timeLeft = string.Empty;

    [ObservableProperty]
    private bool _isEndingSoon;

    [ObservableProperty]
    private bool _isDrawingTask;

    // Which panels are open. A student reading a long task sheet wants the room; one debugging wants
    // the console. The choice is remembered for the next time the application is opened.
    [ObservableProperty]
    private bool _isFilesPanelShown = true;

    [ObservableProperty]
    private bool _isTasksPanelShown = true;

    [ObservableProperty]
    private bool _isConsolePanelShown = true;

    [ObservableProperty]
    private string? _errorMessage;

    // The inline box used both to create a file and to rename one.
    [ObservableProperty]
    private bool _isNamingFile;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string? _fileNameError;

    [ObservableProperty]
    private WorkspaceFileItem? _fileBeingRenamed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingDelete), nameof(IsOverlayShown))]
    private WorkspaceFileItem? _fileToDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayShown))]
    private bool _isConfirmingFinish;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftWindowNote), nameof(HasLeftWindow))]
    private int _leftWindowCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(StopCommand), nameof(SendInputCommand))]
    private bool _isRunning;

    // The whole transcript of the run: what the compiler said, what the program printed, and what the
    // application had to say about it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConsoleCaret))]
    private string _consoleText = string.Empty;

    [ObservableProperty]
    private string _inputText = string.Empty;

    // Bound to the console's caret, which is what keeps the newest line in view.
    public int ConsoleCaret => ConsoleText.Length;

    public ObservableCollection<WorkspaceFileItem> Files { get; } = [];

    public ObservableCollection<WorkspaceFileItem> OpenFiles { get; } = [];

    public ObservableCollection<ExamTaskItem> Tasks { get; } = [];

    // The pages of a PDF task, drawn from the bytes in memory.
    public ObservableCollection<Bitmap> TaskPages { get; } = [];

    public bool HasActiveFile => ActiveFile is not null;

    public bool HasNoActiveFile => ActiveFile is null;

    public string? SelectedTaskText => SelectedTask?.Text;

    public bool IsSelectedTaskTextShown => SelectedTaskText is not null;

    public bool IsSelectedTaskPdf => SelectedTask?.IsPdf == true;

    public bool IsSelectedTaskBinary => SelectedTask is not null && SelectedTaskText is null && !IsSelectedTaskPdf;

    public bool IsConfirmingDelete => FileToDelete is not null;

    public bool IsOverlayShown => IsConfirmingFinish || IsConfirmingDelete;

    public bool HasLeftWindow => LeftWindowCount > 0;

    // Only while the escape hatch is switched on - a testing build. In a real exam there is no way out
    // but finishing, so nothing is shown.
    public bool IsEmergencyExitShown => lockdownOptions.Value.AllowEmergencyExit;

    public string EmergencyExitNote => "Testing build: Ctrl+Alt+Shift+Q saves and closes the application.";

    public string LeftWindowNote => LeftWindowCount == 1
        ? "You left the exam window once."
        : $"You left the exam window {LeftWindowCount} times.";

    public void Initialize(DownloadedExam exam, DownloadedSitting sitting, UnlockedExam unlocked)
    {
        WorkspacePanels panels = preferences.ReadPanels();
        IsFilesPanelShown = panels.Files;
        IsTasksPanelShown = panels.Tasks;
        IsConsolePanelShown = panels.Console;

        _exam = exam;
        _sitting = sitting;
        _unlocked = unlocked;
        _uiContext = SynchronizationContext.Current;

        ExamTitle = exam.Title;
        Details = $"{exam.Subject} · {SittingTimes.Describe(sitting.StartsAt, sitting.EndsAt, timeProvider.LocalTimeZone)}";

        foreach (ExamTaskFile file in unlocked.Files)
        {
            Tasks.Add(new ExamTaskItem(file));
        }

        SelectedTask = Tasks.FirstOrDefault(task => task.Text is not null) ?? Tasks.FirstOrDefault();

        _files = store.Open(exam.ExamId, sitting.SittingId, unlocked.WorkspaceKey);
        _activity = activityLogs.Open(exam.ExamId, sitting.SittingId, unlocked.HandInKey);
        _activity.Write(ActivityKind.ExamOpened, exam.Title);

        List<string> unreadable = [];

        foreach (string name in _files.List())
        {
            try
            {
                Files.Add(CreateItem(name, _files.Read(name)));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Damaged or written under another key. It is left untouched rather than opened as
                // empty, which the first save would then write over.
                unreadable.Add(name);
            }
        }

        if (unreadable.Count > 0)
        {
            ErrorMessage = $"These files could not be read and were left as they are: {string.Join(", ", unreadable)}";
        }

        if (Files.Count > 0)
        {
            Open(Files[0]);
        }

        _autosave = timeProvider.CreateTimer(_ => SaveUnsaved(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _endsAt = sitting.EndsAt;
        UpdateTimeLeft();
        _clock = timeProvider.CreateTimer(_ => OnUiThread(UpdateTimeLeft), null, OneSecond, OneSecond);

        lockdown.ExamWindowLeft += OnExamWindowLeft;
        lockdown.EmergencyExitRequested += OnEmergencyExit;
        lockdown.OutsideContentBlocked += OnOutsideContentBlocked;
        lockdown.Enter();
    }

    // A PDF task is drawn when it is chosen, off the UI thread, and the pages are kept until another
    // task is chosen.
    partial void OnSelectedTaskChanged(ExamTaskItem? value)
    {
        ClearTaskPages();

        if (value?.IsPdf != true)
        {
            return;
        }

        byte[] content = value.Content;
        IsDrawingTask = true;

        _ = Task.Run(() =>
        {
            try
            {
                IReadOnlyList<PdfPage> pages = pdfRenderer.Render(content, PdfScale);

                OnUiThread(() =>
                {
                    IsDrawingTask = false;

                    // The student may have moved on while it was being drawn.
                    if (SelectedTask != value)
                    {
                        return;
                    }

                    foreach (PdfPage page in pages)
                    {
                        TaskPages.Add(ToBitmap(page));
                    }
                });
            }
            catch (Exception exception)
            {
                // Anything at all - a damaged file, a native library that did not load - is shown
                // rather than swallowed on a background thread, where it would look like nothing
                // happening at all.
                OnUiThread(() =>
                {
                    IsDrawingTask = false;
                    ErrorMessage = $"'{value.Name}' could not be shown: {exception.Message}";
                });
            }
        });
    }

    private static WriteableBitmap ToBitmap(PdfPage page)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(page.Width, page.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);

        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            // Row by row: a bitmap's rows can be padded to an alignment the rendered page knows
            // nothing about, and copying the block in one go would then skew the picture.
            int rowBytes = page.Width * 4;

            for (int row = 0; row < page.Height; row++)
            {
                Marshal.Copy(page.Pixels, row * rowBytes, buffer.Address + (row * buffer.RowBytes), rowBytes);
            }
        }

        return bitmap;
    }

    private void ClearTaskPages()
    {
        foreach (Bitmap page in TaskPages)
        {
            page.Dispose();
        }

        TaskPages.Clear();
    }

    private void UpdateTimeLeft()
    {
        TimeSpan left = _endsAt - timeProvider.GetUtcNow();

        if (left <= TimeSpan.Zero)
        {
            TimeLeft = "The sitting has ended";
            IsEndingSoon = true;

            return;
        }

        TimeLeft = left.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00} left")
            : string.Create(CultureInfo.InvariantCulture, $"{left.Minutes:00}:{left.Seconds:00} left");

        IsEndingSoon = left <= TimeSpan.FromMinutes(15);

        // Said once, in the console, so it is not missed while the student is reading the tasks.
        foreach (int minutes in WarningMinutes)
        {
            if (left <= TimeSpan.FromMinutes(minutes) && _lastWarningMinutes != minutes)
            {
                _lastWarningMinutes = minutes;
                Append($"{minutes} minutes left of this sitting.");

                break;
            }
        }
    }

    [RelayCommand]
    private void Open(WorkspaceFileItem? file)
    {
        if (file is null)
        {
            return;
        }

        if (!OpenFiles.Contains(file))
        {
            OpenFiles.Add(file);
        }

        Activate(file);
    }

    [RelayCommand]
    private void CloseTab(WorkspaceFileItem file)
    {
        int index = OpenFiles.IndexOf(file);

        if (index < 0)
        {
            return;
        }

        OpenFiles.RemoveAt(index);

        if (ActiveFile == file)
        {
            Activate(OpenFiles.Count == 0 ? null : OpenFiles[Math.Min(index, OpenFiles.Count - 1)]);
        }
    }

    // Ctrl+S: nothing is waiting for the autosave any more.
    [RelayCommand]
    private void Save() => SaveUnsaved();

    [RelayCommand]
    private void ToggleFilesPanel() => IsFilesPanelShown = !IsFilesPanelShown;

    [RelayCommand]
    private void ToggleTasksPanel() => IsTasksPanelShown = !IsTasksPanelShown;

    [RelayCommand]
    private void ToggleConsolePanel() => IsConsolePanelShown = !IsConsolePanelShown;

    partial void OnIsFilesPanelShownChanged(bool value) => RememberPanels();

    partial void OnIsTasksPanelShownChanged(bool value) => RememberPanels();

    partial void OnIsConsolePanelShownChanged(bool value) => RememberPanels();

    private void RememberPanels() =>
        preferences.WritePanels(new WorkspacePanels(IsFilesPanelShown, IsTasksPanelShown, IsConsolePanelShown));

    [RelayCommand]
    private void StartNewFile()
    {
        FileBeingRenamed = null;
        FileName = string.Empty;
        FileNameError = null;
        IsNamingFile = true;
    }

    [RelayCommand]
    private void StartRename(WorkspaceFileItem file)
    {
        FileBeingRenamed = file;
        FileName = file.Name;
        FileNameError = null;
        IsNamingFile = true;
    }

    [RelayCommand]
    private void CancelNaming()
    {
        IsNamingFile = false;
        FileBeingRenamed = null;
        FileNameError = null;
    }

    [RelayCommand]
    private void ConfirmFileName()
    {
        if (_exam is null || _sitting is null)
        {
            return;
        }

        ApiResult<string> name = WorkspaceFileName.Validate(
            FileName, Files.Select(file => file.Name), FileBeingRenamed?.Name);

        if (!name.IsSuccess)
        {
            FileNameError = name.Error.Message;
            return;
        }

        if (!TryStorage(() => ApplyFileName(name.Value)))
        {
            return;
        }

        CancelNaming();
    }

    private void ApplyFileName(string name)
    {
        if (FileBeingRenamed is { } file)
        {
            if (file.Name == name)
            {
                return;
            }

            // Whatever is still waiting to be saved goes under the old name first, then moves with it.
            SaveUnsaved();
            _files!.Rename(file.Name, name);
            _activity?.Write(ActivityKind.FileRenamed, $"{file.Name} -> {name}");
            file.Name = name;
            SortFiles();
            return;
        }

        _files!.Write(name, string.Empty);
        _activity?.Write(ActivityKind.FileCreated, name);

        WorkspaceFileItem created = CreateItem(name, string.Empty);
        Files.Add(created);
        SortFiles();
        Open(created);
    }

    [RelayCommand]
    private void AskToDelete(WorkspaceFileItem file) => FileToDelete = file;

    [RelayCommand]
    private void CancelDelete() => FileToDelete = null;

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (FileToDelete is not { } file || _exam is null || _sitting is null)
        {
            return;
        }

        lock (_saveLock)
        {
            _unsaved.Remove(file);
        }

        if (TryStorage(() => _files!.Delete(file.Name)))
        {
            _activity?.Write(ActivityKind.FileDeleted, file.Name);
            CloseTab(file);
            Files.Remove(file);
        }

        FileToDelete = null;
    }

    // Compiles what is on screen and runs it, with the toolchain downloaded with the exam.
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (_exam is null || _sitting is null)
        {
            return;
        }

        SaveUnsaved();

        IsRunning = true;
        _running = new CancellationTokenSource();
        _input = Channel.CreateUnbounded<string>();

        try
        {
            Toolchain? toolchain = await PrepareToolchainAsync(_running.Token);

            if (toolchain is null)
            {
                return;
            }

            var request = new RunRequest(
                toolchain,
                store.BuildDirectory(_exam.ExamId, _sitting.SittingId),
                [.. Files.Select(file => new SourceFile(file.Name, file.Document.Text))],
                ProcessorTimeLimit,
                MaxOutputBytes);

            _activity?.Write(ActivityKind.RunStarted, string.Join(", ", request.Sources.Select(source => source.Name)));

            RunResult result = await runner.RunAsync(request, AppendFromRun, _input.Reader, _running.Token);

            _activity?.Write(ActivityKind.RunFinished, DescribeRun(result));

            if (result.Compiled)
            {
                Append(result.ExitCode is { } code
                    ? $"Finished with exit code {code}."
                    : "The program was stopped.");
            }
        }
        finally
        {
            _input.Writer.TryComplete();
            _input = null;
            _running.Dispose();
            _running = null;
            IsRunning = false;
        }
    }

    private static string DescribeRun(RunResult result)
    {
        if (!result.Compiled)
        {
            return "did not compile";
        }

        return result.ExitCode is { } exitCode
            ? string.Create(CultureInfo.InvariantCulture, $"exit code {exitCode}")
            : "stopped";
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _running?.Cancel();

    // What the student types goes to the program's standard input, and into the transcript so the
    // console reads the way a terminal would.
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void SendInput()
    {
        if (_input is null)
        {
            return;
        }

        string line = InputText;
        InputText = string.Empty;

        if (_input.Writer.TryWrite(line))
        {
            AppendText(line + Environment.NewLine);
        }
    }

    [RelayCommand]
    private void ClearConsole()
    {
        _console.Clear();
        ConsoleText = string.Empty;
    }

    // Unpacked on the first run rather than when the workspace opens, so opening the exam is instant.
    private async Task<Toolchain?> PrepareToolchainAsync(CancellationToken cancellationToken)
    {
        if (_toolchains is null)
        {
            Append("Preparing the exam's toolchain.");

            ApiResult<IReadOnlyList<Toolchain>> prepared = await toolchains.PrepareAsync(_exam!, cancellationToken);

            if (!prepared.IsSuccess)
            {
                Append(prepared.Error.Message);

                return null;
            }

            _toolchains = prepared.Value;
        }

        Toolchain? toolchain = _toolchains.FirstOrDefault(candidate => candidate.CanCompileC || candidate.CanCompileCpp);

        if (toolchain is null)
        {
            Append("This exam has no compiler for this computer. Download the exam again while you are online.");
        }
        else if (toolchain.IsFromThisComputer)
        {
            Append("The exam's toolchain has no working compiler here, so the compiler installed on this computer is used instead.");
        }
        else
        {
            Append($"Using {toolchain.Name} {toolchain.Version} from the exam.");
        }

        return toolchain;
    }

    private void AppendFromRun(RunOutputLine line) => OnUiThread(() => AppendText(
        line.Kind == RunOutputKind.Notice ? $"* {line.Text}{Environment.NewLine}" : line.Text));

    private void Append(string notice) => AppendText($"* {notice}{Environment.NewLine}");

    private void AppendText(string text)
    {
        _console.Append(text);

        // Keeping the whole transcript of an endless loop would grow without bound.
        if (_console.Length > MaxConsoleCharacters)
        {
            _console.Remove(0, _console.Length - MaxConsoleCharacters);
        }

        ConsoleText = _console.ToString();
    }

    [RelayCommand]
    private void AskToFinish() => IsConfirmingFinish = true;

    [RelayCommand]
    private void CancelFinish() => IsConfirmingFinish = false;

    // The only way out of the locked workspace. Everything is saved, then sealed: the work becomes one
    // encrypted file and the editable copies are deleted, so from here on there is nothing left to
    // change. A failure at any point keeps the student in the exam rather than leaving with work that
    // is not safely on disk.
    [RelayCommand]
    private async Task ConfirmFinishAsync()
    {
        if (_exam is null || _sitting is null || _unlocked is null)
        {
            return;
        }

        IsConfirmingFinish = false;

        if (_running is not null)
        {
            await _running.CancelAsync();
        }

        if (!SaveUnsaved())
        {
            return;
        }

        _activity?.Write(ActivityKind.ExamFinished);

        ApiResult<SealedSubmission> sealedWork = await submissions.SealAsync(
            _exam, _sitting, _unlocked.WorkspaceKey, _unlocked.HandInKey);

        if (!sealedWork.IsSuccess)
        {
            ErrorMessage = sealedWork.Error.Message;

            return;
        }

        if (!TryStorage(() => store.MarkFinished(_exam.ExamId, _sitting.SittingId, timeProvider.GetUtcNow())))
        {
            return;
        }

        // The files are gone from the workspace now, so nothing may be written back over the seal.
        _unsavedCleared = true;

        lockdown.Exit();
        navigation.NavigateTo<DownloadedExamsViewModel>();
    }

    private WorkspaceFileItem CreateItem(string name, string text)
    {
        var item = new WorkspaceFileItem(name, new TextDocument(text));
        item.Document.TextChanged += (_, _) => OnTextChanged(item);

        return item;
    }

    private void OnTextChanged(WorkspaceFileItem file)
    {
        lock (_saveLock)
        {
            _unsaved[file] = file.Document.Text;
        }

        file.IsDirty = true;
        _autosave?.Change(AutosaveDelay, Timeout.InfiniteTimeSpan);
    }

    // Runs on the autosave timer's thread as well as the UI thread. The lock keeps two saves from
    // writing the same file at once; the flags are updated back on the UI thread.
    private bool SaveUnsaved()
    {
        if (_files is null || _unsavedCleared)
        {
            return false;
        }

        List<WorkspaceFileItem> saved = [];
        string? failure = null;

        lock (_saveLock)
        {
            foreach ((WorkspaceFileItem file, string text) in _unsaved.ToList())
            {
                try
                {
                    _files.Write(file.Name, text);
                    _activity?.Write(ActivityKind.FileSaved, $"{file.Name} ({text.Length} characters)");
                    _unsaved.Remove(file);
                    saved.Add(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Kept in the unsaved set, so the next attempt writes it again.
                    failure = exception.Message;
                }
            }
        }

        OnUiThread(() =>
        {
            lock (_saveLock)
            {
                foreach (WorkspaceFileItem file in saved.Where(file => !_unsaved.ContainsKey(file)))
                {
                    file.IsDirty = false;
                }
            }

            ErrorMessage = failure is null ? null : WorkspaceErrors.SaveFailed(failure).Message;
        });

        return failure is null;
    }

    private bool TryStorage(Action action)
    {
        try
        {
            action();
            ErrorMessage = null;

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = WorkspaceErrors.SaveFailed(exception.Message).Message;

            return false;
        }
    }

    private void Activate(WorkspaceFileItem? file)
    {
        foreach (WorkspaceFileItem open in OpenFiles)
        {
            open.IsActive = open == file;
        }

        ActiveFile = file;
    }

    private void SortFiles()
    {
        List<WorkspaceFileItem> sorted = [.. Files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)];

        for (int index = 0; index < sorted.Count; index++)
        {
            int current = Files.IndexOf(sorted[index]);

            if (current != index)
            {
                Files.Move(current, index);
            }
        }
    }

    private void OnExamWindowLeft(object? sender, EventArgs e) => OnUiThread(() =>
    {
        LeftWindowCount++;
        _activity?.Write(ActivityKind.ExamWindowLeft);
    });

    // Said plainly, because otherwise a paste that does nothing looks like a broken application.
    private void OnOutsideContentBlocked(object? sender, EventArgs e) => OnUiThread(() =>
    {
        _activity?.Write(ActivityKind.OutsideContentBlocked);
        ErrorMessage = "Nothing can be brought into the exam from outside. Copying and pasting inside the exam works as usual.";
    });

    // The testing escape hatch closes the application; the work is written first, and the sitting is
    // deliberately not marked as finished, so the exam can be opened again with the same code.
    private void OnEmergencyExit(object? sender, EventArgs e)
    {
        _running?.Cancel();
        SaveUnsaved();
    }

    private void OnUiThread(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
        }
        else
        {
            _uiContext.Post(_ => action(), null);
        }
    }

    public void Dispose()
    {
        lockdown.ExamWindowLeft -= OnExamWindowLeft;
        lockdown.EmergencyExitRequested -= OnEmergencyExit;
        lockdown.OutsideContentBlocked -= OnOutsideContentBlocked;

        _running?.Cancel();

        SaveUnsaved();

        _activity?.Write(ActivityKind.ExamClosed);
        _activity?.Dispose();
        _activity = null;

        _clock?.Dispose();
        _clock = null;
        ClearTaskPages();

        _files?.Dispose();
        _files = null;

        _autosave?.Dispose();
        _autosave = null;

        // Leaving by any other way than finishing would be a bug, but it must never leave the
        // student stuck in a fullscreen window with nothing to do.
        lockdown.Exit();

        _unlocked?.Dispose();
        _unlocked = null;
    }

    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(1);

    // Processor time, not wall-clock time: a program waiting for the student to type is never stopped
    // for being slow, while an endless loop is.
    private static readonly TimeSpan ProcessorTimeLimit = TimeSpan.FromSeconds(10);

    private const long MaxOutputBytes = 1024 * 1024;

    private const int MaxConsoleCharacters = 200_000;

    // Twice the page's own size: sharp enough to read when the task panel is widened.
    private const int PdfScale = 2;

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private static readonly int[] WarningMinutes = [5, 15];
}
