using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Credentials;

namespace SecureExamIDE.Client.Services.Unlock;

// The client half of the sealing scheme, run on exam day with no connection:
//
//   KEK     = Argon2id(normalised code, header salt, header parameters)
//   K       = AES-256-GCM-open(KEK, wrapped key)     fails  => wrong code
//   archive = AES-256-GCM-open(K, package.bin)        fails  => damaged package
//   tasks   = the files in the zip archive
//
// It also derives the key the workspace encrypts the student's own files with, from the content key
// and this computer's id. Nothing has to be stored for it: typing the code again after a restart
// derives the same key, and the same folder copied to another computer derives a different one.
internal sealed class PackageUnlocker(IMachineIdentity machineIdentity) : IPackageUnlocker
{
    public Task<ApiResult<UnlockedExam>> UnlockAsync(
        string packagePath,
        string headerPath,
        string expectedPackageSha256,
        string typedCode,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Unlock(packagePath, headerPath, expectedPackageSha256, typedCode), cancellationToken);

    private ApiResult<UnlockedExam> Unlock(
        string packagePath,
        string headerPath,
        string expectedPackageSha256,
        string typedCode)
    {
        string code = OneTimeCode.Normalize(typedCode);

        if (code.Length != OneTimeCode.Length)
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.IncompleteCode);
        }

        if (!File.Exists(packagePath) || !File.Exists(headerPath))
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.Missing);
        }

        PackageHeader? header = ReadHeader(headerPath);

        if (header is null)
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.Damaged);
        }

        if (!IsSupported(header))
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.Unsupported);
        }

        byte[] ciphertext = File.ReadAllBytes(packagePath);

        // Checked before the slow key derivation: a package that is not the one downloaded is
        // reported as damaged, not as a wrong code the student would keep retyping.
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(ciphertext)), expectedPackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.Damaged);
        }

        byte[] keyEncryptionKey = [];
        byte[] contentKey = [];
        byte[] archive = [];

        try
        {
            keyEncryptionKey = DeriveKeyEncryptionKey(code, header);

            if (!TryOpen(keyEncryptionKey, header.WrappedKey, header.WrappedKeyNonce, header.WrappedKeyTag, null, out contentKey))
            {
                return ApiResult.Failure<UnlockedExam>(UnlockErrors.WrongCode);
            }

            if (!TryOpen(contentKey, null, header.PackageNonce, header.PackageTag, ciphertext, out archive))
            {
                return ApiResult.Failure<UnlockedExam>(UnlockErrors.Damaged);
            }

            IReadOnlyList<ExamTaskFile>? files = ReadArchive(archive);

#pragma warning disable CA2000 // Ownership passes to the caller, which disposes it when the exam is locked again.
            return files is null
                ? ApiResult.Failure<UnlockedExam>(UnlockErrors.Damaged)
                : ApiResult.Success(new UnlockedExam(files, DeriveWorkspaceKey(contentKey)));
#pragma warning restore CA2000
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyEncryptionKey);
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(archive);
        }
    }

    // Separate from the content key, so the exam package's own key is never the one sitting in
    // memory for every save, and bound to this machine through the same id the credential store uses.
    private byte[] DeriveWorkspaceKey(byte[] contentKey) => HKDF.DeriveKey(
        HashAlgorithmName.SHA256,
        contentKey,
        KeySizeBytes,
        salt: Encoding.UTF8.GetBytes(machineIdentity.GetMachineId()),
        info: WorkspaceKeyInfo);

    private static PackageHeader? ReadHeader(string headerPath)
    {
        try
        {
            return JsonSerializer.Deserialize<PackageHeader>(File.ReadAllBytes(headerPath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The KDF parameters come from a downloaded file, so they are bounded: a header asking for
    // gigabytes of memory would otherwise stall or crash the student's laptop in the exam room.
    private static bool IsSupported(PackageHeader header) =>
        header.Version == SupportedVersion &&
        header.Algorithm == "AES-256-GCM" &&
        header.Kdf == "Argon2id" &&
        header.CodeNormalization == OneTimeCode.NormalizationRule &&
        header.KdfIterations is >= 1 and <= 10 &&
        header.KdfMemoryKib is >= 1024 and <= MaxKdfMemoryKib &&
        header.KdfParallelism is >= 1 and <= 16;

    private static byte[] DeriveKeyEncryptionKey(string normalizedCode, PackageHeader header)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(normalizedCode))
        {
            Salt = Convert.FromBase64String(header.KdfSalt),
            Iterations = header.KdfIterations,
            MemorySize = header.KdfMemoryKib,
            DegreeOfParallelism = header.KdfParallelism
        };

        return argon2.GetBytes(KeySizeBytes);
    }

    private static bool TryOpen(
        byte[] key,
        string? ciphertextBase64,
        string nonceBase64,
        string tagBase64,
        byte[]? ciphertextBytes,
        out byte[] plaintext)
    {
        plaintext = [];

        try
        {
            byte[] ciphertext = ciphertextBytes ?? Convert.FromBase64String(ciphertextBase64!);
            byte[] nonce = Convert.FromBase64String(nonceBase64);
            byte[] tag = Convert.FromBase64String(tagBase64);
            byte[] output = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, output);

            plaintext = output;
            return true;
        }
        catch (Exception exception) when (exception is AuthenticationTagMismatchException or CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    // Entries become flat file names, so a crafted name cannot point anywhere, and the total size is
    // capped so a compressed bomb cannot exhaust memory.
    private static List<ExamTaskFile>? ReadArchive(byte[] archive)
    {
        try
        {
            using var stream = new MemoryStream(archive, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            List<ExamTaskFile> files = [];
            long total = 0;

            foreach (ZipArchiveEntry entry in zip.Entries.Take(MaxEntries))
            {
                string name = Path.GetFileName(entry.FullName.Replace('\\', '/'));

                if (name.Length == 0)
                {
                    continue;
                }

                total += entry.Length;

                if (total > MaxUnpackedBytes)
                {
                    return null;
                }

                using Stream content = entry.Open();
                using var buffer = new MemoryStream((int)entry.Length);
                content.CopyTo(buffer);

                files.Add(new ExamTaskFile(name, buffer.ToArray()));
            }

            return [.. files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static readonly byte[] WorkspaceKeyInfo = "SecureExamIDE workspace key v1"u8.ToArray();

    private const int SupportedVersion = 1;
    private const int KeySizeBytes = 32;
    private const int MaxKdfMemoryKib = 1024 * 1024;
    private const int MaxEntries = 1000;
    private const long MaxUnpackedBytes = 512L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
