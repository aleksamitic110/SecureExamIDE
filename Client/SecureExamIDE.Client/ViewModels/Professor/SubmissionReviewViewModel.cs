using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Files;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Review;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One student's work, opened. The files are read here; the activity log is read beside them, with what
// the chain says about it; and nothing is written to this computer unless the professor exports it.
public sealed partial class SubmissionReviewViewModel(
    ISessionService session,
    INavigationService navigation,
    IFilePicker filePicker) : SignedInViewModelBase(session, navigation), IDisposable
{
    private Guid _sittingId;
    private string _examTitle = string.Empty;
    private OpenedSubmission? _opened;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFile))]
    private ReviewedFileItem? _selectedFile;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<ReviewedFileItem> Files { get; } = [];

    public ObservableCollection<ActivityEventItem> ActivityEvents { get; } = [];

    public void Initialize(Guid sittingId, string examTitle, SubmissionItem submission, OpenedSubmission opened)
    {
        _sittingId = sittingId;
        _examTitle = examTitle;
        _opened = opened;

        Student = submission.Student;
        Details = $"{examTitle} · handed in {submission.When} {submission.From}";
        IsLate = submission.IsLate;

        foreach (ExamTaskFile file in opened.Files)
        {
            Files.Add(new ReviewedFileItem(file));
        }

        SelectedFile = Files.FirstOrDefault();

        foreach (ActivityEvent recorded in opened.ActivityLog.Events)
        {
            ActivityEvents.Add(new ActivityEventItem(recorded));
        }
    }

    public string Student { get; private set; } = string.Empty;

    public string Details { get; private set; } = string.Empty;

    public bool IsLate { get; private set; }

    public bool HasSelectedFile => SelectedFile is not null;

    // What the professor can rely on: the bytes matched the digests the server measured when the work
    // arrived, and the log's own chain is unbroken.
    public string EvidenceNote => DescribeEvidence();

    public bool IsActivityLogComplete => _opened?.ActivityLog.IsComplete != false;

    public bool HasSuspiciousEvents => ActivityEvents.Any(recorded => recorded.IsSuspicious);

    // Counted up front, because the thing a professor wants to know first is whether this log is worth
    // reading at all. Every count is stated even when it is zero, so a count is never mistaken for
    // another kind.
    public string SuspiciousSummary
    {
        get
        {
            if (!HasSuspiciousEvents)
            {
                return string.Empty;
            }

            int pastes = ActivityEvents.Count(recorded => recorded.Kind == nameof(ActivityKind.OutsideContentBlocked));
            int leaves = ActivityEvents.Count(recorded => recorded.Kind == nameof(ActivityKind.ExamWindowLeft));
            int reopenings = ActivityEvents.Count(recorded => recorded.Kind == nameof(ActivityKind.ExamReopened));

            return string.Create(
                CultureInfo.CurrentCulture,
                $"Worth a look: {Plural.Format(pastes, "blocked paste")}, {Plural.Format(leaves, "window leave")}, {Plural.Format(reopenings, "reopening")}.");
        }
    }

    public string ActivityLogWarning => _opened is null || _opened.ActivityLog.IsComplete
        ? string.Empty
        : string.Create(
            CultureInfo.InvariantCulture,
            $"The log stops being readable after event {_opened.ActivityLog.Events.Count} of {_opened.ActivityLog.LineCount} lines. Lines were removed, reordered or altered; what is shown above that point is still what it says it is.");

    private string DescribeEvidence()
    {
        if (_opened is null)
        {
            return string.Empty;
        }

        string chain = _opened.ActivityLog.IsComplete ? "chain unbroken" : "chain broken - see below";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"The downloaded files matched the digests recorded on arrival. Activity log: {_opened.ActivityLog.Events.Count} events, {chain}.");
    }

    // Deliberate, and the only way a plain copy of a solution reaches the professor's computer - for
    // compiling it or running it through a marking script.
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_opened is null)
        {
            return;
        }

        string? folder = await filePicker.PickFolderAsync("Choose a folder for this student's files");

        if (folder is null)
        {
            return;
        }

        ErrorMessage = null;

        try
        {
            string target = Path.Combine(folder, FolderName());
            Directory.CreateDirectory(target);

            foreach (ExamTaskFile file in _opened.Files)
            {
                await File.WriteAllBytesAsync(Path.Combine(target, Path.GetFileName(file.Name)), file.Content);
            }

            await File.WriteAllTextAsync(Path.Combine(target, "activity-log.txt"), DescribeActivityLog(), Encoding.UTF8);

            StatusMessage = $"Saved to {target}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = ReviewErrors.CannotExport(exception.Message).Message;
        }
    }

    // A plain-text copy of the log goes with the files, since it is the other half of the evidence.
    private string DescribeActivityLog()
    {
        var text = new StringBuilder();

        text.AppendLine(CultureInfo.InvariantCulture, $"{Student} - {_examTitle}");
        text.AppendLine(EvidenceNote);

        if (HasSuspiciousEvents)
        {
            text.AppendLine(SuspiciousSummary);
            text.AppendLine("Those lines are marked with ! below.");
        }

        text.AppendLine();

        // The kind is written as the log recorded it, and the marker is the only thing added: this file
        // is the half of the evidence that leaves the application, so it stays comparable between
        // students and between sittings.
        foreach (ActivityEventItem recorded in ActivityEvents)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{(recorded.IsSuspicious ? "!" : " ")} {recorded.Sequence,4}  {recorded.Stamp}  {recorded.Kind}  {recorded.Detail}");
        }

        if (!IsActivityLogComplete)
        {
            text.AppendLine();
            text.AppendLine(ActivityLogWarning);
        }

        return text.ToString();
    }

    private string FolderName()
    {
        string student = new(Student.Where(character => char.IsAsciiLetterOrDigit(character) || character is ' ' or '-').ToArray());

        return student.Trim().Length == 0 ? _sittingId.ToString("N") : student.Trim();
    }

    [RelayCommand]
    private void Back() => Navigation.NavigateTo<SubmissionsViewModel>(page => page.Initialize(_sittingId, _examTitle, Details));

    // The opened files are wiped when the screen is left, like the student's own unlocked exam.
    public void Dispose()
    {
        foreach (ReviewedFileItem file in Files)
        {
            file.Wipe();
        }

        _opened = null;
    }
}
