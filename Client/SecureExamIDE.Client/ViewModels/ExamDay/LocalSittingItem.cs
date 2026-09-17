using System.Globalization;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Exams;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

// A downloaded sitting as the exam-day screens show it, worked out from the local record alone.
public sealed class LocalSittingItem
{
    public LocalSittingItem(DownloadedExam exam, DownloadedSitting sitting, TimeProvider timeProvider)
    {
        Exam = exam;
        Sitting = sitting;

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
