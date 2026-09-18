using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Submission;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

// A downloaded sitting as the exam-day screens show it, worked out from the local record alone.
public sealed partial class LocalSittingItem : ObservableObject
{
    public LocalSittingItem(
        DownloadedExam exam,
        DownloadedSitting sitting,
        TimeProvider timeProvider,
        SealedSubmission? submission = null)
    {
        Exam = exam;
        Sitting = sitting;
        _submission = submission;

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (now > sitting.EndsAt)
        {
            Status = "Ended";
        }
        else
        {
            Status = now < sitting.StartsAt ? "Upcoming" : "In progress";
        }

        When = SittingTimes.Describe(sitting.StartsAt, sitting.EndsAt, timeProvider.LocalTimeZone);
        IsInProgress = Status == "In progress";
    }

    public DownloadedExam Exam { get; }

    public DownloadedSitting Sitting { get; }

    public string ExamTitle => Exam.Title;

    public string Subject => $"{Exam.Subject} · {Exam.ProfessorName}";

    public string When { get; }

    public string Status { get; }

    public bool IsInProgress { get; }

    public string PackageSize => ByteSize.Format(Sitting.PackageSizeBytes);

    // What became of the work: sealed on this computer, and then handed in when a connection allowed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSealed), nameof(IsHandedIn), nameof(IsWaitingToHandIn), nameof(SubmissionStatus), nameof(CanEnter))]
    private SealedSubmission? _submission;

    public bool IsSealed => Submission is not null;

    public bool IsHandedIn => Submission?.IsHandedIn == true;

    public bool IsWaitingToHandIn => IsSealed && !IsHandedIn;

    public bool CanEnter => !IsSealed;

    public string SubmissionStatus => Submission switch
    {
        null => string.Empty,
        { HandedInAt: { } handedIn } => $"Handed in {handedIn.ToLocalTime():ddd d MMM, HH:mm}",
        _ => "Sealed on this computer, waiting to be handed in"
    };
}

public static class SittingTimes
{
    // "Fri 18 Sep 2026, 10:00 - 12:00" in the computer's own time zone.
    public static string Describe(DateTimeOffset startsAt, DateTimeOffset endsAt, TimeZoneInfo timeZone)
    {
        DateTime starts = TimeZoneInfo.ConvertTime(startsAt, timeZone).DateTime;
        DateTime ends = TimeZoneInfo.ConvertTime(endsAt, timeZone).DateTime;

        return starts.Date == ends.Date
            ? string.Create(CultureInfo.InvariantCulture, $"{starts:ddd d MMM yyyy}, {starts:HH:mm} - {ends:HH:mm}")
            : string.Create(CultureInfo.InvariantCulture, $"{starts:ddd d MMM yyyy, HH:mm} - {ends:ddd d MMM yyyy, HH:mm}");
    }
}
