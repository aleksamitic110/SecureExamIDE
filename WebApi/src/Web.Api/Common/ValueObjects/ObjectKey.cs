using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

// The key an object is stored under in the object store. Note what this type does *not* promise:
// that the key belongs to any particular exam, or that anything is stored under it. Both are
// contextual and stay where they can actually be checked - in the handler, against the exam id and
// against the store itself.
public sealed record ObjectKey
{
    public const int MaxLength = 512;

    private ObjectKey(string value) => Value = value;

    public string Value { get; }

    public static Result<ObjectKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ObjectKey>(ValueObjectsErrors.Empty(nameof(ObjectKey)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<ObjectKey>(ValueObjectsErrors.TooLong(nameof(ObjectKey), MaxLength));
        }

        return new ObjectKey(trimmed);
    }

    internal static ObjectKey FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
