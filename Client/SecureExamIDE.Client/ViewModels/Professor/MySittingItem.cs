using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One sitting in the professor's list. Times arrive in UTC and are shown in the computer's local
// time, the same way the student's screens do it.
public sealed class MySittingItem(MySitting sitting, DateTimeOffset now)
{
    public Guid Id => sitting.Id;

    public string ExamTitle => sitting.ExamTitle;

    public string When =>
        $"{sitting.StartsAt.ToLocalTime():ddd d MMM yyyy HH:mm} – {sitting.EndsAt.ToLocalTime():HH:mm}";

    public string Status => Describe();

    public string Handed => sitting.SubmissionCount == 1
        ? "1 solution handed in"
        : $"{sitting.SubmissionCount} solutions handed in";

    // Cancelling one that has already ended is allowed on purpose: that is also how a professor
    // stops late uploads. Only one already cancelled has nothing left to do.
    public bool CanCancel => !sitting.IsCancelled;

    private string Describe()
    {
        if (sitting.IsCancelled)
        {
            return "Cancelled";
        }

        if (now < sitting.StartsAt)
        {
            return "Upcoming";
        }

        return now <= sitting.EndsAt ? "In progress" : "Ended";
    }
}
