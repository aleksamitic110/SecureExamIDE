using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

// A person's given or family name.
public sealed record PersonName
{
    public const int MaxLength = 100;

    private PersonName(string value) => Value = value;

    public string Value { get; }

    public static Result<PersonName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<PersonName>(ValueObjectsErrors.Empty(nameof(PersonName)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<PersonName>(ValueObjectsErrors.TooLong(nameof(PersonName), MaxLength));
        }

        return new PersonName(trimmed);
    }

    internal static PersonName FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
