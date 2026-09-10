using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

// A validated, normalised e-mail address. Normalising here rather than in each handler is what
// makes it impossible to register as "Aleksa@x.com" and then fail to log in as "aleksa@x.com".
public sealed record Email
{
    public const int MaxLength = 256;

    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<Email>(ValueObjectsErrors.Empty(nameof(Email)));
        }

        // Lowercase is the conventional normal form for an e-mail address; CA1308's preference for
        // ToUpperInvariant applies to values being normalised for comparison, not for storage.
#pragma warning disable CA1308
        string normalized = value.Trim().ToLowerInvariant();
#pragma warning restore CA1308

        if (normalized.Length > MaxLength)
        {
            return Result.Failure<Email>(ValueObjectsErrors.TooLong(nameof(Email), MaxLength));
        }

        int atIndex = normalized.IndexOf('@', StringComparison.Ordinal);

        if (atIndex <= 0 || atIndex >= normalized.Length - 1)
        {
            return Result.Failure<Email>(ValueObjectsErrors.InvalidFormat(nameof(Email)));
        }

        return new Email(normalized);
    }

    // Rehydrates a value this application already stored. Used only by EF Core when materialising
    // rows: the database is trusted, so re-running validation on every read would only cost time.
    internal static Email FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
