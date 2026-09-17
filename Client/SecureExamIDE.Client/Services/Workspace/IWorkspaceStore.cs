namespace SecureExamIDE.Client.Services.Workspace;

// The student's own files for one sitting, kept next to the sitting's package:
//
//   exams/{examId}/sittings/{sittingId}/workspace/files/{name}
//   exams/{examId}/sittings/{sittingId}/workspace/finished.json
//
// Plain files for now. Encrypting them at rest is the next part of the workspace step, and it is
// meant to happen behind this interface, so the screens do not change when it does.
//
// Synchronous on purpose: source files are a few kilobytes, and the final save before handing in
// has to be finished, not scheduled.
public interface IWorkspaceStore
{
    IReadOnlyList<string> ListFiles(Guid examId, Guid sittingId);

    string ReadFile(Guid examId, Guid sittingId, string name);

    void WriteFile(Guid examId, Guid sittingId, string name, string text);

    void RenameFile(Guid examId, Guid sittingId, string name, string newName);

    void DeleteFile(Guid examId, Guid sittingId, string name);

    bool IsFinished(Guid examId, Guid sittingId);

    void MarkFinished(Guid examId, Guid sittingId, DateTimeOffset finishedAt);
}
