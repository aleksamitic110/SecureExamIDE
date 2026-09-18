namespace SecureExamIDE.Client.Services.Submission;

// What this computer holds for a finished sitting, written beside the sealed solution as
// submission.json. It is what the exam-day screens read to say whether the work is still waiting to
// be handed in.
public sealed record SealedSubmission(
    Guid ExamId,
    Guid SittingId,
    string ExamTitle,
    DateTimeOffset SealedAt,
    long SolutionSizeBytes,
    int ActivityEventCount,
    DateTimeOffset? HandedInAt)
{
    public bool IsHandedIn => HandedInAt is not null;
}
