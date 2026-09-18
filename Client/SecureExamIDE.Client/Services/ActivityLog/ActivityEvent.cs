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
    ExamFinished
}

public sealed record ActivityEvent(int Sequence, DateTimeOffset At, ActivityKind Kind, string? Detail);
