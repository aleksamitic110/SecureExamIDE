using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Session;

public enum StartupOutcome
{
    // No usable credential on this computer: show the welcome screen.
    SignedOut,

    // The computer is bound to an account whose address still needs its code.
    EmailNotVerified,

    SignedIn,

    // The server could not be reached or refused for another reason. The credential is kept, so
    // retrying later can still succeed.
    Failed
}

// CanWorkOffline: the server is unreachable, but this computer is signed in and knows whose it is,
// so the exams already downloaded can still be opened - the situation in every exam room.
public sealed record StartupResult(
    StartupOutcome Outcome,
    string? Email = null,
    ApiError? Error = null,
    bool CanWorkOffline = false);
