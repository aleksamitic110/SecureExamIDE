using System.Text;

namespace SecureExamIDE.Client.Services.Unlock;

public static class OneTimeCode
{
    // The rule the header states in CodeNormalization, which this client checks it understands
    // before trusting it: a header describing any other rule is refused rather than unlocked with
    // a key that could never match.
    public const string NormalizationRule = "uppercase; O->0, I->1, L->1; drop anything outside 0-9 A-Z";

    // A code has 20 characters; "O" for zero and "l" for one are the mistakes people make copying
    // one off a whiteboard, so they are corrected, and dashes and spaces are ignored.
    public const int Length = 20;

    public static string Normalize(string typed)
    {
        var normalized = new StringBuilder(typed.Length);

        foreach (char character in typed.ToUpperInvariant())
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
}
