using System.Text.RegularExpressions;
using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

// A SHA-256 digest in lowercase hexadecimal. Wrapping it stops a truncated, uppercase or
// differently-encoded digest from reaching a column that other code assumes is comparable.
public sealed partial record Sha256Hash
{
    public const int Length = 64;

    private Sha256Hash(string value) => Value = value;

    public string Value { get; }

    public static Result<Sha256Hash> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<Sha256Hash>(ValueObjectsErrors.Empty(nameof(Sha256Hash)));
        }

        string trimmed = value.Trim();

        if (!Pattern().IsMatch(trimmed))
        {
            return Result.Failure<Sha256Hash>(ValueObjectsErrors.InvalidFormat(nameof(Sha256Hash)));
        }

        return new Sha256Hash(trimmed);
    }

    internal static Sha256Hash FromTrusted(string value) => new(value);

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Pattern();
}
