using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Exams;

// A version string such as "13.2.0" or "21.0.2-tem". The character set is restricted rather than
// left free-form because name and version together identify a dependency uniquely within an exam:
// allowing whitespace would let "1.0" and "1.0 " both be stored as different rows.
public sealed record DependencyVersion
{
    public const int MaxLength = 50;

    private DependencyVersion(string value) => Value = value;

    public string Value { get; }

    public static Result<DependencyVersion> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<DependencyVersion>(ValueObjectsErrors.Empty(nameof(DependencyVersion)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<DependencyVersion>(
                ValueObjectsErrors.TooLong(nameof(DependencyVersion), MaxLength));
        }

        foreach (char character in trimmed)
        {
            if (!IsAllowed(character))
            {
                return Result.Failure<DependencyVersion>(
                    ValueObjectsErrors.InvalidFormat(nameof(DependencyVersion)));
            }
        }

        return new DependencyVersion(trimmed);
    }

    internal static DependencyVersion FromTrusted(string value) => new(value);

    public override string ToString() => Value;

    private static bool IsAllowed(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '+';
}
