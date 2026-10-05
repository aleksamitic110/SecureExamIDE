namespace SecureExamIDE.Client.Services.ActivityLog;

// What the student did, as the log records it. Deliberately coarse: this is evidence of how an exam
// was taken, not a keystroke recorder. Nothing here holds the student's code - the solution itself is
// what is handed in - except the names of their own files.
public enum ActivityKind
{
    ExamOpened,
    ExamClosed,
    FileCreated,
    FileRenamed,
    FileDeleted,
    FileSaved,
    RunStarted,
    RunFinished,
    OutsideContentBlocked,
    ExamWindowLeft,
    ExamFinished,

    // The exam was opened again after it had already been opened once: the application was closed or
    // stopped in the middle of a sitting, and what happened in between was not recorded.
    ExamReopened
}

public sealed record ActivityEvent(int Sequence, DateTimeOffset At, ActivityKind Kind, string? Detail);

// Which kinds suggest a student tried to get round the exam rather than simply work in it: content was
// brought in from outside and blocked, the exam window was left, or the application was closed in the
// middle of the sitting and opened again. Everything else - files, saves, runs, opening and
// finishing - is ordinary work.
//
// The rule lives beside the kinds themselves so the professor's screen and the exported log cannot
// drift apart on what counts as worth a second look. Neither is proof of anything: a window can be
// left by a notification stealing focus, which is exactly why a professor is shown them to judge.
public static class ActivityKinds
{
    public static bool SuggestsCheating(this ActivityKind kind) =>
        kind is ActivityKind.OutsideContentBlocked or ActivityKind.ExamWindowLeft or ActivityKind.ExamReopened;
}
