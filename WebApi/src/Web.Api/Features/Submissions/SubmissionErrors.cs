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
        "No uploaded content was found for the provided object key");

    public static readonly Error ObjectKeyNotOwned = Error.Forbidden(
        "Submissions.ObjectKeyNotOwned",
        "The provided object key does not belong to this student and session");

    public static readonly Error EmptyContent = Error.Problem(
        "Submissions.EmptyContent",
        "The uploaded solution is empty");
}
