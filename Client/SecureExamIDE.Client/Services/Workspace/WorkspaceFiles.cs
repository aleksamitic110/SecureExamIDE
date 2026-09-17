using System.Security.Cryptography;
using System.Text;
using SecureExamIDE.Client.Services.Storage;

namespace SecureExamIDE.Client.Services.Workspace;

// One sitting's files while the exam is open. Every file is sealed with AES-256-GCM under the key
// the unlock produced:
//
//   SEIW1 | nonce (12) | tag (16) | ciphertext
//
// The file's own name and the sitting's id are the authenticated data, so a file cannot be passed
// off as another file, or as a file from another sitting, without the tag failing. What this
// protects against is the exam itself being edited from outside the application - with another
// editor, or by pasting in something written elsewhere - which the blocked paste would otherwise
// leave wide open. It is not a defence against the student's own operating system, which the
// student controls.
internal sealed class WorkspaceFiles : IWorkspaceFiles
{
    private readonly string _directory;
    private readonly Guid _sittingId;
    private readonly byte[] _key;

    public WorkspaceFiles(string directory, Guid sittingId, byte[] key)
    {
        _directory = directory;
        _sittingId = sittingId;

        // A copy of its own, so wiping the unlocked exam does not pull the key away mid-save.
        _key = (byte[])key.Clone();
    }

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        // Anything that is not a valid workspace name - a leftover "main.c.tmp" from a save cut
        // short by a crash, above all - is not the student's file and is not shown.
        return Directory.EnumerateFiles(_directory)
            .Select(System.IO.Path.GetFileName)
            .OfType<string>()
            .Where(WorkspaceFileName.IsSafe)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string Read(string name)
    {
        byte[] stored = File.ReadAllBytes(PathOf(name));

        if (!stored.AsSpan().StartsWith(Magic))
        {
            // Written by the first version of the workspace, before the files were encrypted. It is
            // read as it stands and sealed the next time it is saved.
            return Encoding.UTF8.GetString(stored);
        }

        if (stored.Length < HeaderSize)
        {
            throw new InvalidDataException($"'{name}' is too short to be a workspace file.");
        }

        byte[] plaintext = new byte[stored.Length - HeaderSize];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                stored.AsSpan(Magic.Length, NonceSize),
                stored.AsSpan(HeaderSize),
                stored.AsSpan(Magic.Length + NonceSize, TagSize),
                plaintext,
                AssociatedData(name));

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException($"'{name}' could not be read.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Write(string name, string text)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(text);
        byte[] stored = new byte[HeaderSize + plaintext.Length];

        try
        {
            Magic.CopyTo(stored);
            RandomNumberGenerator.Fill(stored.AsSpan(Magic.Length, NonceSize));

            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(
                stored.AsSpan(Magic.Length, NonceSize),
                plaintext,
                stored.AsSpan(HeaderSize),
                stored.AsSpan(Magic.Length + NonceSize, TagSize),
                AssociatedData(name));

            Directory.CreateDirectory(_directory);
            AtomicFile.Write(PathOf(name), stored);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    // The name is part of what is authenticated, so renaming means sealing the text again.
    public void Rename(string name, string newName)
    {
        string text = Read(name);

        Write(newName, text);
        Delete(name);
    }

    public void Delete(string name) => File.Delete(PathOf(name));

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);

    private byte[] AssociatedData(string name) =>
        Encoding.UTF8.GetBytes($"SecureExamIDE workspace v1|{_sittingId:N}|{name}");

    // Also the store's own guard, so a name that skipped the screen's validation still cannot
    // reach outside the workspace folder.
    private string PathOf(string name) =>
        WorkspaceFileName.IsSafe(name)
            ? System.IO.Path.Combine(_directory, name)
            : throw new ArgumentException($"'{name}' is not a valid workspace file name.", nameof(name));

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static ReadOnlySpan<byte> Magic => "SEIW1"u8;

    private static readonly int HeaderSize = Magic.Length + NonceSize + TagSize;
}
