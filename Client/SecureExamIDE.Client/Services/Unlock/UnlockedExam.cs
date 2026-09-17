using System.Security.Cryptography;

namespace SecureExamIDE.Client.Services.Unlock;

// The exam tasks after unlocking. They live in memory only: nothing decrypted is written to disk,
// and disposing wipes the bytes rather than leaving them for the garbage collector.
public sealed class UnlockedExam(IReadOnlyList<ExamTaskFile> files) : IDisposable
{
    public IReadOnlyList<ExamTaskFile> Files => files;

    public void Dispose()
    {
        foreach (ExamTaskFile file in files)
        {
            CryptographicOperations.ZeroMemory(file.Content);
        }
    }
}

public sealed record ExamTaskFile(string Name, byte[] Content);
