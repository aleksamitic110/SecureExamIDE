using System.Text;
using System.Text.Json;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Storage;

namespace SecureExamIDE.Client.Services.Workspace;

internal sealed class WorkspaceStore(ILocalExamLibrary library) : IWorkspaceStore
{
    public IReadOnlyList<string> ListFiles(Guid examId, Guid sittingId)
    {
        string directory = FilesDirectory(examId, sittingId);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        // Anything that is not a valid workspace name - a leftover "main.c.tmp" from a save cut
        // short by a crash, above all - is not the student's file and is not shown.
        return Directory.EnumerateFiles(directory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(WorkspaceFileName.IsSafe)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string ReadFile(Guid examId, Guid sittingId, string name) =>
        File.ReadAllText(FilePath(examId, sittingId, name), Encoding.UTF8);

    public void WriteFile(Guid examId, Guid sittingId, string name, string text)
    {
        string path = FilePath(examId, sittingId, name);

        Directory.CreateDirectory(FilesDirectory(examId, sittingId));
        AtomicFile.Write(path, Utf8WithoutBom.GetBytes(text));
    }

    public void RenameFile(Guid examId, Guid sittingId, string name, string newName) =>
        File.Move(FilePath(examId, sittingId, name), FilePath(examId, sittingId, newName));

    public void DeleteFile(Guid examId, Guid sittingId, string name) =>
        File.Delete(FilePath(examId, sittingId, name));

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

    private string FilePath(Guid examId, Guid sittingId, string name) =>
        WorkspaceFileName.IsSafe(name)
            ? Path.Combine(FilesDirectory(examId, sittingId), name)
            : throw new ArgumentException($"'{name}' is not a valid workspace file name.", nameof(name));

    private sealed record FinishedMarker(DateTimeOffset FinishedAt);

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
