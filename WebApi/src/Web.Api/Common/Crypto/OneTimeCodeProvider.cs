using System.Security.Cryptography;
using System.Text;

namespace Web.Api.Common.Crypto;

internal sealed class OneTimeCodeProvider : IOneTimeCodeProvider
{
    public string Generate()
    {
        var code = new StringBuilder(CharacterCount + GroupCount - 1);

        for (int i = 0; i < CharacterCount; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                code.Append('-');
            }

            code.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return code.ToString();
    }

    // Hashed after the same normalisation the key derivation uses, so a professor who types the
    // code back with different casing or spacing is still recognised.
    public string Hash(string oneTimeCode) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(CryptoService.NormalizeCode(oneTimeCode))));

    // Crockford's alphabet: no I, L, O or U, so a code read aloud or copied off a whiteboard has
    // no ambiguous characters and cannot accidentally spell a word.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // 20 characters from a 32-symbol alphabet is 100 bits. That is deliberately generous for
    // something typed by hand: the header travels with the package, so an attacker can guess
    // offline for as long as they like, and only the code's own entropy limits them.
    private const int CharacterCount = 20;
    private const int GroupSize = 4;
    private const int GroupCount = CharacterCount / GroupSize;
}
