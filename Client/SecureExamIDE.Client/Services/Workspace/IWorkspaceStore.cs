namespace SecureExamIDE.Client.Services.Workspace;

// The student's own files for one sitting, kept next to the sitting's package:
//
//   exams/{examId}/sittings/{sittingId}/workspace/files/{name}
//   exams/{examId}/sittings/{sittingId}/workspace/finished.json
//
// The files are encrypted, which is why they are reached through Open: the key comes from unlocking
// the package with the one-time code, so the work can only be read back inside the exam.
//
// Synchronous on purpose: source files are a few kilobytes, and the final save before handing in
// has to be finished, not scheduled.
public interface IWorkspaceStore
{
    IWorkspaceFiles Open(Guid examId, Guid sittingId, byte[] key);

    // Asked before the code is typed, so it needs no key.
    bool IsFinished(Guid examId, Guid sittingId);

    void MarkFinished(Guid examId, Guid sittingId, DateTimeOffset finishedAt);
}

public interface IWorkspaceFiles : IDisposable
{
    IReadOnlyList<string> List();

    // Throws InvalidDataException when the file cannot be decrypted - damaged, or written under a
    // different key. The screen reports it and leaves the file alone rather than overwriting it.
    string Read(string name);

    void Write(string name, string text);

    void Rename(string name, string newName);

    void Delete(string name);
}
