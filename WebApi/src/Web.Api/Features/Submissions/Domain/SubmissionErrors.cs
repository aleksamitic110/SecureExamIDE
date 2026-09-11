using Web.Api.Common;

namespace Web.Api.Features.Submissions;

public static class SubmissionErrors
{
    public static Error NotFound(Guid submissionId) => Error.NotFound(
        "Submissions.NotFound",
        $"The submission with the Id = '{submissionId}' was not found");

    // Submitting is the one action that must come from the machine that sat the exam, so an
    // ordinary password login is refused even though it is a perfectly valid token.
    public static readonly Error DeviceTokenRequired = Error.Forbidden(
        "Submissions.DeviceTokenRequired",
        "Submitting requires a device token obtained from POST /auth/device-token");

    public static readonly Error AlreadySubmitted = Error.Conflict(
        "Submissions.AlreadySubmitted",
        "A solution has already been submitted for this session and cannot be replaced");

    public static readonly Error SessionNotStarted = Error.Conflict(
        "Submissions.SessionNotStarted",
        "The session has not started yet");

    public static readonly Error SolutionNotUploaded = Error.Problem(
        "Submissions.SolutionNotUploaded",
        "No uploaded solution was found for the provided object key");

    public static readonly Error ActivityLogNotUploaded = Error.Problem(
        "Submissions.ActivityLogNotUploaded",
        "No uploaded activity log was found for the provided object key");

    // Covers both a key minted for another student and a key minted for the other part of this
    // student's own submission - a solution cannot stand in for the log, or the log for the solution.
    public static readonly Error ObjectKeyNotOwned = Error.Forbidden(
        "Submissions.ObjectKeyNotOwned",
        "An object key was not issued for this part of this student's submission");

    public static readonly Error EmptySolution = Error.Problem(
        "Submissions.EmptySolution",
        "The uploaded solution is empty");

    public static readonly Error EmptyActivityLog = Error.Problem(
        "Submissions.EmptyActivityLog",
        "The uploaded activity log is empty");
}
