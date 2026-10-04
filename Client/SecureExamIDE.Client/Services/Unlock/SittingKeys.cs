using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace SecureExamIDE.Client.Services.Unlock;

// Everything a sitting's one-time code leads to, in one place, because two sides of the application
// now need it: the **student's** unlock on exam day, and the **professor's** opening of what was
// handed in. A second copy of this derivation is the last thing this project should have - the two
// would drift, and the professor would be left holding a key that opens nothing.
//
//   KEK          = Argon2id(normalised code, header salt and parameters)
//   content key  = AES-256-GCM-open(KEK, wrapped key)        fails => wrong code
//   workspace key = HKDF(content key, salt = machine id)     the student's files at rest
//   hand-in key   = HKDF(content key)                        the sealed solution and the log
internal static class SittingKeys
{
    public const int KeySizeBytes = 32;

    public static PackageHeader? ReadHeader(byte[] header)
    {
        try
        {
            return JsonSerializer.Deserialize<PackageHeader>(header, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The KDF parameters come from a downloaded file, so they are bounded: a header asking for
    // gigabytes of memory would otherwise stall or crash the student's laptop in the exam room.
    public static bool IsSupported(PackageHeader header) =>
        header.Version == SupportedVersion &&
        header.Algorithm == "AES-256-GCM" &&
        header.Kdf == "Argon2id" &&
        header.CodeNormalization == OneTimeCode.NormalizationRule &&
        header.KdfIterations is >= 1 and <= 10 &&
        header.KdfMemoryKib is >= 1024 and <= MaxKdfMemoryKib &&
        header.KdfParallelism is >= 1 and <= 16;

    // The slow step, and the one that decides whether the code was right.
    public static bool TryDeriveContentKey(PackageHeader header, string normalizedCode, out byte[] contentKey)
    {
        byte[] keyEncryptionKey = DeriveKeyEncryptionKey(normalizedCode, header);

        try
        {
            return TryOpen(
                keyEncryptionKey, header.WrappedKey, header.WrappedKeyNonce, header.WrappedKeyTag, null, out contentKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyEncryptionKey);
        }
    }

    // Bound to this machine through the same id the credential store uses, so a copied folder is
    // inert on another computer, and so the exam package's own key is not the one held in memory for
    // every autosave.
    public static byte[] DeriveWorkspaceKey(byte[] contentKey, string machineId) => HKDF.DeriveKey(
        HashAlgorithmName.SHA256,
        contentKey,
        KeySizeBytes,
        salt: Encoding.UTF8.GetBytes(machineId),
        info: WorkspaceKeyInfo);

    // No machine id here, on purpose: the professor derives this same key from the one-time code and
    // the package header, and that is what lets them open what a student handed in.
    public static byte[] DeriveHandInKey(byte[] contentKey) => HKDF.DeriveKey(
        HashAlgorithmName.SHA256,
        contentKey,
        KeySizeBytes,
        salt: null,
        info: HandInKeyInfo);

    public static bool TryOpen(
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
    // capped so a compressed bomb cannot exhaust memory. Used for the exam's tasks and for the zip
    // inside a sealed solution, which is read on the professor's computer and deserves the same care.
    public static List<ExamTaskFile>? ReadArchive(byte[] archive)
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

            return [.. files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

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

    private static readonly byte[] WorkspaceKeyInfo = "SecureExamIDE workspace key v1"u8.ToArray();

    private static readonly byte[] HandInKeyInfo = "SecureExamIDE hand-in key v1"u8.ToArray();

    private const int SupportedVersion = 1;
    private const int MaxKdfMemoryKib = 1024 * 1024;
    private const int MaxEntries = 1000;
    private const long MaxUnpackedBytes = 512L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
