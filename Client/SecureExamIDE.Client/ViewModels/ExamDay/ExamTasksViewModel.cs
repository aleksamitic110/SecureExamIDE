using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Unlock;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

// The unlocked exam: its task files, read from memory. This is the doorway the workspace will
// replace in the next step; for now it proves the package opened and lets the tasks be read.
public sealed partial class ExamTasksViewModel(INavigationService navigation, TimeProvider timeProvider)
    : ViewModelBase, IDisposable
{
    private UnlockedExam? _unlocked;

    [ObservableProperty]
    private string _examTitle = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText), nameof(IsSelectedTextShown), nameof(IsSelectedBinary))]
    private ExamTaskItem? _selectedTask;

    public ObservableCollection<ExamTaskItem> Tasks { get; } = [];

    public string? SelectedText => SelectedTask?.Text;

    public bool IsSelectedTextShown => SelectedText is not null;

    public bool IsSelectedBinary => SelectedTask is not null && SelectedText is null;

    public void Initialize(DownloadedExam exam, DownloadedSitting sitting, UnlockedExam unlocked)
    {
        _unlocked = unlocked;

        ExamTitle = exam.Title;
        Details = $"{exam.Subject} · {SittingTimes.Describe(sitting.StartsAt, sitting.EndsAt, timeProvider.LocalTimeZone)}";

        foreach (ExamTaskFile file in unlocked.Files)
        {
            Tasks.Add(new ExamTaskItem(file));
        }

        SelectedTask = Tasks.FirstOrDefault(task => task.Text is not null) ?? Tasks.FirstOrDefault();
    }

    // Locking again wipes the decrypted tasks from memory; the code is needed to open them again.
    [RelayCommand]
    private void Lock() => navigation.NavigateTo<DownloadedExamsViewModel>();

    public void Dispose()
    {
        _unlocked?.Dispose();
        _unlocked = null;
    }
}
