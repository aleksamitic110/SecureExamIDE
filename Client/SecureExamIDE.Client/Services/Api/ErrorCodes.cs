namespace SecureExamIDE.Client.Services.Api;

// The server error codes the client reacts to rather than just displaying.
public static class ErrorCodes
{
    public const string EmailNotVerified = "Users.EmailNotVerified";

    // Login answers an unknown address and a wrong password with the same error.
    public const string UserNotFoundByEmail = "Users.NotFoundByEmail";

    public const string InvalidDeviceCredential = "Devices.InvalidCredential";

    // The professor cancelled the sitting after the student saw it listed.
    public const string SittingCancelled = "ExamSessions.Cancelled";

    // The server takes one submission per student per sitting, ever.
    public const string AlreadySubmitted = "Submissions.AlreadySubmitted";
}
