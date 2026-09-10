using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Web.Api.Common.Crypto;

// Implements the scheme the whole offline promise rests on:
//
//   K         = random 256-bit content key
//   KEK       = Argon2id(one-time code, salt)
//   wrapped_K = AES-256-GCM(KEK, K)
//   package   = AES-256-GCM(K, plaintext)
//
// The server keeps neither K nor the code, so a stolen database yields nothing openable. GCM is
// what makes a wrong code fail cleanly rather than producing garbage, and makes tampering with a
// downloaded package detectable.
internal sealed class CryptoService : ICryptoService
{
    public SealedPackage Seal(byte[] plaintext, string oneTimeCode)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(oneTimeCode);

        byte[] contentKey = RandomNumberGenerator.GetBytes(KeySizeBytes);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] keyEncryptionKey = DeriveKeyEncryptionKey(oneTimeCode, salt);

        try
        {
            (byte[] wrappedKey, byte[] wrapNonce, byte[] wrapTag) = Encrypt(keyEncryptionKey, contentKey);
            (byte[] ciphertext, byte[] nonce, byte[] tag) = Encrypt(contentKey, plaintext);

            var header = new PackageHeader
            {
                KdfIterations = Iterations,
                KdfMemoryKib = MemoryKib,
                KdfParallelism = Parallelism,
                KdfSalt = Convert.ToBase64String(salt),
                WrappedKey = Convert.ToBase64String(wrappedKey),
                WrappedKeyNonce = Convert.ToBase64String(wrapNonce),
                WrappedKeyTag = Convert.ToBase64String(wrapTag),
                PackageNonce = Convert.ToBase64String(nonce),
                PackageTag = Convert.ToBase64String(tag)
            };

            return new SealedPackage(ciphertext, header);
        }
        finally
        {
            // The key material must not outlive the call: anything still on the heap could reach a
            // crash dump or a swapped page.
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(keyEncryptionKey);
        }
    }

    // Exposed so that the client and the tests fold a typed code exactly the way sealing did.
    // "O" for zero and "l" for one are the mistakes people actually make when copying a code off a
    // whiteboard, so they are corrected rather than rejected.
    public static string NormalizeCode(string code)
    {
        var normalized = new StringBuilder(code.Length);

        foreach (char character in code.ToUpperInvariant())
        {
            char mapped = character switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => character
            };

            if (char.IsAsciiLetterOrDigit(mapped))
            {
                normalized.Append(mapped);
            }
        }

        return normalized.ToString();
    }

    public static byte[] DeriveKeyEncryptionKey(string oneTimeCode, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(NormalizeCode(oneTimeCode)))
        {
            Salt = salt,
            Iterations = Iterations,
            MemorySize = MemoryKib,
            DegreeOfParallelism = Parallelism
        };

        return argon2.GetBytes(KeySizeBytes);
    }

    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(byte[] key, byte[] plaintext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return (ciphertext, nonce, tag);
    }

    private const int KeySizeBytes = 32;
    private const int SaltSizeBytes = 16;

    // OWASP's recommended Argon2id baseline: 64 MiB and three passes is affordable once on a
    // student's laptop, and expensive enough to make guessing the code offline impractical.
    private const int Iterations = 3;
    private const int MemoryKib = 65_536;
    private const int Parallelism = 1;
}
