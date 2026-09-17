using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using SecureExamIDE.Client.Services.Storage;

namespace SecureExamIDE.Client.Services.Credentials;

// The Windows store. The credential is protected with DPAPI for the current user: only this user
// account on this machine can unprotect it, which is exactly the binding a device credential needs,
// and Windows manages the key. A file copied to another computer or another account opens nothing.
[SupportedOSPlatform("windows")]
internal sealed class DpapiCredentialStore(string directory) : ICredentialStore
{
    private string CredentialPath => Path.Combine(directory, "device-credential.bin");

    public async Task<StoredCredential?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CredentialPath))
        {
            return null;
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(CredentialPath, cancellationToken);
        byte[] plaintext;

        try
        {
            plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Edited, or protected by another user or machine.
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredCredential>(plaintext);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task SaveAsync(StoredCredential credential, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(credential);

        try
        {
            byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            await AtomicFile.WriteAsync(CredentialPath, protectedBytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        File.Delete(CredentialPath);

        return Task.CompletedTask;
    }

    // Extra entropy ties the blob to this application: another program running as the same user
    // cannot unprotect it without also knowing these bytes.
    private static readonly byte[] Entropy = "SecureExamIDE device credential v1"u8.ToArray();
}
