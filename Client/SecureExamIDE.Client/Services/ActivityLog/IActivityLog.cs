namespace SecureExamIDE.Client.Services.ActivityLog;

// The local, encrypted record of how an exam was taken. It is written as the exam happens and handed
// in together with the solution, which is the only way it ever leaves the computer.
//
// Opened with the same key the unlock derived, so - like the student's files - it can only be read
// inside the exam, and the server stores it without being able to read it at all.
public interface IActivityLogStore
{
    IActivityLog Open(Guid examId, Guid sittingId, byte[] key);

    // Where the file is, for handing it in.
    string PathOf(Guid examId, Guid sittingId);
}

public interface IActivityLog : IDisposable
{
    void Write(ActivityKind kind, string? detail = null);

    // Reading it back is for the client's own checks and for tests; the server never sees plaintext.
    IReadOnlyList<ActivityEvent> Read();
}
