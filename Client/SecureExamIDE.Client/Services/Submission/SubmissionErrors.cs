using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Submission;

internal static class SubmissionErrors
{
    public static ApiError CannotSeal(string detail) => new(
        0,
        "Submission.CannotSeal",
        $"Your work could not be sealed: {detail}. Nothing has been changed; try finishing again.",
        []);

    public static readonly ApiError Missing = new(
        0,
        "Submission.Missing",
        "The sealed solution for this sitting is not on this computer.",
        []);
}
