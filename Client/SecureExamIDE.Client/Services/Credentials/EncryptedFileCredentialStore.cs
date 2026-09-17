using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureExamIDE.Client.Services.Storage;

namespace SecureExamIDE.Client.Services.Credentials;

// The fallback where no OS secret store is wired up yet - Linux during development of the Windows
// client; libsecret belongs to the linux-client branch. The credential is sealed with AES-256-GCM
// under a key derived from random key material kept beside it and this machine's id, so the folder
// copied to another computer opens nothing, and GCM makes any edit to the file detectable.
internal sealed class EncryptedFileCredentialStore(string directory, IMachineIdentity machineIdentity) : ICredentialStore
{
    private string CredentialPath => Path.Combine(directory, "device-credential.sealed");

    private string KeyMaterialPath => Path.Combine(directory, "device-credential.key");

    public async Task<StoredCredential?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CredentialPath) || !File.Exists(KeyMaterialPath))
        {
            return null;
        }

        byte[] sealedBytes = await File.ReadAllBytesAsync(CredentialPath, cancellationToken);
        byte[] keyMaterial = await File.ReadAllBytesAsync(KeyMaterialPath, cancellationToken);

        if (sealedBytes.Length <= NonceSize + TagSize || keyMaterial.Length != KeySize)
        {
            return null;
        }

        byte[] key = DeriveKey(keyMaterial);
        byte[] plaintext = new byte[sealedBytes.Length - NonceSize - TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                sealedBytes.AsSpan(0, NonceSize),
                sealedBytes.AsSpan(NonceSize + TagSize),
                sealedBytes.AsSpan(NonceSize, TagSize),
                plaintext);

            return JsonSerializer.Deserialize<StoredCredential>(plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            // Edited, or copied from another machine: treated exactly like no credential at all.
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task SaveAsync(StoredCredential credential, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        byte[] keyMaterial = File.Exists(KeyMaterialPath)
            ? await File.ReadAllBytesAsync(KeyMaterialPath, cancellationToken)
            : RandomNumberGenerator.GetBytes(KeySize);

        if (keyMaterial.Length != KeySize)
        {
            keyMaterial = RandomNumberGenerator.GetBytes(KeySize);
        }

        byte[] key = DeriveKey(keyMaterial);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(credential);
        byte[] sealedBytes = new byte[NonceSize + TagSize + plaintext.Length];

        try
        {
            RandomNumberGenerator.Fill(sealedBytes.AsSpan(0, NonceSize));

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(
                    sealedBytes.AsSpan(0, NonceSize),
                    plaintext,
                    sealedBytes.AsSpan(NonceSize + TagSize),
                    sealedBytes.AsSpan(NonceSize, TagSize));
            }

            await AtomicFile.WriteAsync(KeyMaterialPath, keyMaterial, cancellationToken);
            await AtomicFile.WriteAsync(CredentialPath, sealedBytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        File.Delete(CredentialPath);
        File.Delete(KeyMaterialPath);

        return Task.CompletedTask;
    }

    // The machine id is the salt: the same key material yields a different key on another computer.
    private byte[] DeriveKey(byte[] keyMaterial) => HKDF.DeriveKey(
        HashAlgorithmName.SHA256,
        keyMaterial,
        KeySize,
        salt: Encoding.UTF8.GetBytes(machineIdentity.GetMachineId()),
        info: "SecureExamIDE device credential v1"u8.ToArray());

    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
}
