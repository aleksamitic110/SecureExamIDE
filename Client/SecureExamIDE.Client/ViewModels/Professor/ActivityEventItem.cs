using System.Globalization;
using SecureExamIDE.Client.Services.ActivityLog;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One event out of a student's activity log, as the review screen shows it. The kinds that suggest
// cheating are marked so the screen can colour them: a professor marking a class should not have to
// read every line of every log to find the few lines worth reading.
public sealed class ActivityEventItem(ActivityEvent recorded)
{
    public int Sequence => recorded.Sequence;

    // The time alone is enough in the list, which is read next to the hand-in's own date.
    public string Time => recorded.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    // The exported copy carries the date too: it is read on its own, long after.
    public string Stamp => recorded.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // The name as the log recorded it, which is what the exported copy keeps: it is evidence, and a
    // reworded one is harder to compare against another student's.
    public string Kind => recorded.Kind.ToString();

    // The suspicious kinds are recorded under names of their own, so they are given words here
    // rather than leaving a professor to work out what "OutsideContentBlocked" meant.
    public string Description => recorded.Kind switch
    {
        ActivityKind.OutsideContentBlocked => "Paste from outside blocked",
        ActivityKind.ExamWindowLeft => "Left the exam window",
        ActivityKind.ExamReopened => "Exam reopened after the application was closed",
        _ => Kind
    };

    public string? Detail => recorded.Detail;

    public bool IsSuspicious => recorded.Kind.SuggestsCheating();
}
