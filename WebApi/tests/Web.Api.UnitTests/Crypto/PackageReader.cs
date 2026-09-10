using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Web.Api.Common.Crypto;

namespace Web.Api.UnitTests.Crypto;

// Deliberately a second, independent implementation of the unlock path rather than a call back
// into the server's own code. The claim being tested is that a client holding only package.bin,
// package.hdr and the typed code can recover the exam with no network and no help from the server,
// so the test has to do exactly that and nothing more.
internal static class PackageReader
{
    public static byte[] Open(byte[] ciphertext, PackageHeader header, string typedCode)
    {
        byte[] keyEncryptionKey = DeriveKek(typedCode, header);
        byte[] contentKey = Decrypt(
            keyEncryptionKey,
            Convert.FromBase64String(header.WrappedKey),
            Convert.FromBase64String(header.WrappedKeyNonce),
            Convert.FromBase64String(header.WrappedKeyTag));

        return Decrypt(
            contentKey,
            ciphertext,
            Convert.FromBase64String(header.PackageNonce),
            Convert.FromBase64String(header.PackageTag));
    }

    private static byte[] DeriveKek(string typedCode, PackageHeader header)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(Normalize(typedCode)))
        {
            Salt = Convert.FromBase64String(header.KdfSalt),
            Iterations = header.KdfIterations,
            MemorySize = header.KdfMemoryKib,
            DegreeOfParallelism = header.KdfParallelism
        };

        return argon2.GetBytes(32);
    }

    // Follows the rule the header states in CodeNormalization, rather than importing the server's
    // helper - if the two ever drift apart, this is where it shows.
    private static string Normalize(string code)
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

    private static byte[] Decrypt(byte[] key, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }
}
