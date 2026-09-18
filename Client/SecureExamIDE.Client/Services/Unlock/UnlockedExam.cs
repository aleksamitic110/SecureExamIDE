using System.Security.Cryptography;

namespace SecureExamIDE.Client.Services.Unlock;

// The exam after unlocking: the task files, which live in memory only and are never written to disk,
// and the two keys the exam is worked with. Both come from the package's content key, so both exist
// only while the exam is open with the right code, and disposing wipes everything.
//
// They differ in one deliberate way:
//
// - **WorkspaceKey** mixes in this computer's machine id. The student's files at rest are its
//   business alone, and a copied folder must be inert on another computer.
// - **HandInKey** does not. What is handed in - the sealed solution and the activity log - has to be
//   readable by the **professor**, who holds the one-time code and nothing of this computer.
public sealed class UnlockedExam(IReadOnlyList<ExamTaskFile> files, byte[] workspaceKey, byte[] handInKey) : IDisposable
{
    public IReadOnlyList<ExamTaskFile> Files => files;

    public byte[] WorkspaceKey => workspaceKey;

    public byte[] HandInKey => handInKey;

    public void Dispose()
    {
        foreach (ExamTaskFile file in files)
        {
            CryptographicOperations.ZeroMemory(file.Content);
        }

        CryptographicOperations.ZeroMemory(workspaceKey);
        CryptographicOperations.ZeroMemory(handInKey);
    }
}

public sealed record ExamTaskFile(string Name, byte[] Content);
