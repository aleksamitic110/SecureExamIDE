using System.Security.Cryptography;

namespace SecureExamIDE.Client.Services.Unlock;

// The exam after unlocking: the task files, which live in memory only and are never written to disk,
// and the key the student's own files are encrypted with while they work.
//
// The workspace key is derived from the package's content key, so it exists only while the exam is
// open with the right code. Disposing wipes everything rather than leaving it for the collector.
public sealed class UnlockedExam(IReadOnlyList<ExamTaskFile> files, byte[] workspaceKey) : IDisposable
{
    public IReadOnlyList<ExamTaskFile> Files => files;

    public byte[] WorkspaceKey => workspaceKey;

    public void Dispose()
    {
        foreach (ExamTaskFile file in files)
        {
            CryptographicOperations.ZeroMemory(file.Content);
        }

        CryptographicOperations.ZeroMemory(workspaceKey);
    }
}

public sealed record ExamTaskFile(string Name, byte[] Content);
