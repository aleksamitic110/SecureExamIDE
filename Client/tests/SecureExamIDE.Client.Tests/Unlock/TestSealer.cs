using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using SecureExamIDE.Client.Services.Unlock;

namespace SecureExamIDE.Client.Tests.Unlock;

// Seals a package the way the server's CryptoService does, written out again independently rather
// than shared, so a drift between the two shows up as a failing test. The KDF parameters are cheap
// here; the header records them, and the unlocker must follow whatever the header says. The live
// check against packages the real server sealed covers the production parameters.
internal static class TestSealer
{
    public const string Code = "B34K-X088-D12W-75Y6-MJQX";

    public static (byte[] Package, byte[] Header, string Sha256) Seal(
        IReadOnlyDictionary<string, string> files,
        string code = Code,
        Func<PackageHeader, PackageHeader>? alterHeader = null)
    {
        byte[] archive = Zip(files);
        byte[] contentKey = RandomNumberGenerator.GetBytes(32);
        byte[] salt = RandomNumberGenerator.GetBytes(16);

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(Normalize(code)))
        {
            Salt = salt,
            Iterations = Iterations,
            MemorySize = MemoryKib,
            DegreeOfParallelism = 1
        };
        byte[] keyEncryptionKey = argon2.GetBytes(32);

        (byte[] wrappedKey, byte[] wrapNonce, byte[] wrapTag) = Encrypt(keyEncryptionKey, contentKey);
        (byte[] ciphertext, byte[] nonce, byte[] tag) = Encrypt(contentKey, archive);

        var header = new PackageHeader(
            1, "AES-256-GCM", "Argon2id", Iterations, MemoryKib, 1,
            "uppercase; O->0, I->1, L->1; drop anything outside 0-9 A-Z",
            Convert.ToBase64String(salt),
            Convert.ToBase64String(wrappedKey), Convert.ToBase64String(wrapNonce), Convert.ToBase64String(wrapTag),
            Convert.ToBase64String(nonce), Convert.ToBase64String(tag));

        header = alterHeader?.Invoke(header) ?? header;

        return (
            ciphertext,
            JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions),
            Convert.ToHexStringLower(SHA256.HashData(ciphertext)));
    }

    private static byte[] Zip(IReadOnlyDictionary<string, string> files)
    {
        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in files)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }

    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(byte[] key, byte[] plaintext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return (ciphertext, nonce, tag);
    }

    private static string Normalize(string code)
    {
        var normalized = new StringBuilder();

        foreach (char c in code.ToUpperInvariant())
        {
            char mapped = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };

            if (char.IsAsciiLetterOrDigit(mapped))
            {
                normalized.Append(mapped);
            }
        }

        return normalized.ToString();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int Iterations = 1;
    private const int MemoryKib = 1024;
}
