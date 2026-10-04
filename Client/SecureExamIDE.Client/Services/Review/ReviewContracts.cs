using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Unlock;

namespace SecureExamIDE.Client.Services.Review;

// A submission after the professor has opened it with the sitting's one-time code: the student's own
// files, and the activity log that came with them.
//
// Nothing here is written to disk. The professor may export the files deliberately, and that is the
// only way a plain copy of a solution reaches their computer.
public sealed record OpenedSubmission(
    IReadOnlyList<ExamTaskFile> Files,
    ActivityLogContents ActivityLog)
{
    // The digests the server measured on arrival matched what was downloaded: this is the file the
    // student handed in, byte for byte.
    public bool DigestsVerified { get; init; } = true;
}
