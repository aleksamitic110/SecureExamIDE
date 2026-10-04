using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One handed-in solution as the review list shows it. Everything here comes from the server; the
// student's work itself stays sealed until the professor opens it with the sitting's code.
public sealed class SubmissionItem(SubmissionSummary submission)
{
    public SubmissionSummary Submission => submission;

    public string Student => submission.StudentIndexNumber is { Length: > 0 } index
        ? $"{submission.StudentFirstName} {submission.StudentLastName} ({index})"
        : $"{submission.StudentFirstName} {submission.StudentLastName}";

    public string Email => submission.StudentEmail;

    // The machine it came from, which is the bound device the work was handed in with.
    public string From => $"from {submission.DeviceName}";

    public string When => $"{submission.SubmittedAt.ToLocalTime():ddd d MMM yyyy HH:mm}";

    public bool IsLate => submission.SubmittedAfterSessionEnded;

    public string Sizes =>
        $"solution {ByteSize.Format(submission.SolutionSizeBytes)} · log {ByteSize.Format(submission.ActivityLogSizeBytes)}";
}
