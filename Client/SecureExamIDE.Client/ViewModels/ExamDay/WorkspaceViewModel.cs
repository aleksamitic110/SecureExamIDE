using System.Collections.ObjectModel;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Lockdown;
using SecureExamIDE.Client.Services.Navigation;
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
    IExamLockdown lockdown,
    TimeProvider timeProvider) : ViewModelBase, IDisposable
{
    private readonly Lock _saveLock = new();
    private readonly Dictionary<WorkspaceFileItem, string> _unsaved = [];

    private DownloadedExam? _exam;
    private DownloadedSitting? _sitting;
    private UnlockedExam? _unlocked;
    private IWorkspaceFiles? _files;
    private ITimer? _autosave;
    private SynchronizationContext? _uiContext;

    [ObservableProperty]
    private string _examTitle = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFile), nameof(HasNoActiveFile))]
    private WorkspaceFileItem? _activeFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTaskText), nameof(IsSelectedTaskTextShown), nameof(IsSelectedTaskBinary))]
    private ExamTaskItem? _selectedTask;

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

    public ObservableCollection<WorkspaceFileItem> Files { get; } = [];

    public ObservableCollection<WorkspaceFileItem> OpenFiles { get; } = [];

    public ObservableCollection<ExamTaskItem> Tasks { get; } = [];

    public bool HasActiveFile => ActiveFile is not null;

    public bool HasNoActiveFile => ActiveFile is null;

    public string? SelectedTaskText => SelectedTask?.Text;

    public bool IsSelectedTaskTextShown => SelectedTaskText is not null;

    public bool IsSelectedTaskBinary => SelectedTask is not null && SelectedTaskText is null;

    public bool IsConfirmingDelete => FileToDelete is not null;

    public bool IsOverlayShown => IsConfirmingFinish || IsConfirmingDelete;

    public bool HasLeftWindow => LeftWindowCount > 0;

    public string LeftWindowNote => LeftWindowCount == 1
        ? "You left the exam window once."
        : $"You left the exam window {LeftWindowCount} times.";

    public void Initialize(DownloadedExam exam, DownloadedSitting sitting, UnlockedExam unlocked)
    {
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

        lockdown.ExamWindowLeft += OnExamWindowLeft;
        lockdown.Enter();
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
            file.Name = name;
            SortFiles();
            return;
        }

        _files!.Write(name, string.Empty);

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
            CloseTab(file);
            Files.Remove(file);
        }

        FileToDelete = null;
    }

    [RelayCommand]
    private void AskToFinish() => IsConfirmingFinish = true;

    [RelayCommand]
    private void CancelFinish() => IsConfirmingFinish = false;

    // The only way out of the locked workspace. Everything is saved first, and a failed save keeps
    // the student in the exam rather than leaving with work that is not on disk.
    [RelayCommand]
    private void ConfirmFinish()
    {
        if (_exam is null || _sitting is null)
        {
            return;
        }

        IsConfirmingFinish = false;

        bool saved = SaveUnsaved() &&
                     TryStorage(() => store.MarkFinished(_exam.ExamId, _sitting.SittingId, timeProvider.GetUtcNow()));

        if (!saved)
        {
            return;
        }

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
        if (_files is null)
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

    private void OnExamWindowLeft(object? sender, EventArgs e) => OnUiThread(() => LeftWindowCount++);

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

        SaveUnsaved();

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
}
