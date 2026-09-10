using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

// The display name of an uploaded file. Path separators are rejected so a stored name can never be
// interpreted as a path when a client writes it to disk.
public sealed record FileName
{
    public const int MaxLength = 255;

    private FileName(string value) => Value = value;

    public string Value { get; }

    public static Result<FileName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<FileName>(ValueObjectsErrors.Empty(nameof(FileName)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<FileName>(ValueObjectsErrors.TooLong(nameof(FileName), MaxLength));
        }

        if (trimmed.Contains('/', StringComparison.Ordinal) ||
            trimmed.Contains('\\', StringComparison.Ordinal) ||
            trimmed is "." or "..")
        {
            return Result.Failure<FileName>(ValueObjectsErrors.InvalidFormat(nameof(FileName)));
        }

        return new FileName(trimmed);
    }

    internal static FileName FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
