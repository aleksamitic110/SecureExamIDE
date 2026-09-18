using System.Text.Json;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Storage;

namespace SecureExamIDE.Client.Services.Workspace;

internal sealed class WorkspaceStore(ILocalExamLibrary library) : IWorkspaceStore
{
    public IWorkspaceFiles Open(Guid examId, Guid sittingId, byte[] key) =>
        new WorkspaceFiles(FilesDirectory(examId, sittingId), sittingId, key);

    public string BuildDirectory(Guid examId, Guid sittingId) =>
        Path.Combine(WorkspaceDirectory(examId, sittingId), "build");

    public bool IsFinished(Guid examId, Guid sittingId) =>
        File.Exists(FinishedPath(examId, sittingId));

    // Only a local marker for now. Until the solution is sealed at hand-in (step 7) it stops the
    // application from reopening a finished sitting, not a determined student with a file manager.
    public void MarkFinished(Guid examId, Guid sittingId, DateTimeOffset finishedAt)
    {
        Directory.CreateDirectory(WorkspaceDirectory(examId, sittingId));

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new FinishedMarker(finishedAt), JsonOptions);

        AtomicFile.Write(FinishedPath(examId, sittingId), json);
    }

    private string WorkspaceDirectory(Guid examId, Guid sittingId) =>
        Path.Combine(library.SittingDirectory(examId, sittingId), "workspace");

    private string FilesDirectory(Guid examId, Guid sittingId) =>
        Path.Combine(WorkspaceDirectory(examId, sittingId), "files");

    private string FinishedPath(Guid examId, Guid sittingId) =>
        Path.Combine(WorkspaceDirectory(examId, sittingId), "finished.json");

    private sealed record FinishedMarker(DateTimeOffset FinishedAt);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
