using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

// An IANA media type such as application/pdf.
public sealed record ContentType
{
    public const int MaxLength = 100;

    public static readonly ContentType Default = new("application/octet-stream");

    private ContentType(string value) => Value = value;

    public string Value { get; }

    public static Result<ContentType> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ContentType>(ValueObjectsErrors.Empty(nameof(ContentType)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<ContentType>(ValueObjectsErrors.TooLong(nameof(ContentType), MaxLength));
        }

        int slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);

        if (slashIndex <= 0 || slashIndex >= trimmed.Length - 1)
        {
            return Result.Failure<ContentType>(ValueObjectsErrors.InvalidFormat(nameof(ContentType)));
        }

        return new ContentType(trimmed);
    }

    internal static ContentType FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
