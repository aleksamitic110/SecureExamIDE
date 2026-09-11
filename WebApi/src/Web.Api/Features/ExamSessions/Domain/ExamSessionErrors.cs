using Web.Api.Common;

namespace Web.Api.Features.ExamSessions;

public static class ExamSessionErrors
{
    public static Error NotFound(Guid sessionId) => Error.NotFound(
        "ExamSessions.NotFound",
        $"The exam session with the Id = '{sessionId}' was not found");

    // Sealing needs the finished set of files, and an unpublished exam is still being edited.
    public static readonly Error ExamNotPublished = Error.Conflict(
        "ExamSessions.ExamNotPublished",
        "A session can only be scheduled for a published exam");

    public static readonly Error EndsBeforeItStarts = Error.Problem(
        "ExamSessions.EndsBeforeItStarts",
        "The session must end after it starts");

    public static readonly Error AlreadyOver = Error.Problem(
        "ExamSessions.AlreadyOver",
        "The session must end in the future");

    public static readonly Error Cancelled = Error.Conflict(
        "ExamSessions.Cancelled",
        "This session has been cancelled");

    public static readonly Error NoFiles = Error.Conflict(
        "ExamSessions.NoFiles",
        "The exam has no files to seal into a package");
}
