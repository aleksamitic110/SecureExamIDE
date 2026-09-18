using SecureExamIDE.Client.Services.Exams;

namespace SecureExamIDE.Client.Services.ActivityLog;

internal sealed class ActivityLogStore(ILocalExamLibrary library, TimeProvider timeProvider) : IActivityLogStore
{
    public IActivityLog Open(Guid examId, Guid sittingId, byte[] key) =>
        new EncryptedActivityLog(PathOf(examId, sittingId), sittingId, key, timeProvider);

    // Next to the student's files, and handed in with them.
    public string PathOf(Guid examId, Guid sittingId) =>
        Path.Combine(library.SittingDirectory(examId, sittingId), "workspace", "activity.log");
}
