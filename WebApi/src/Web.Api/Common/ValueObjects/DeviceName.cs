using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

// The human-readable name of a bound machine, shown in the owner's device list.
public sealed record DeviceName
{
    public const int MaxLength = 100;

    private DeviceName(string value) => Value = value;

    public string Value { get; }

    public static Result<DeviceName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<DeviceName>(ValueObjectsErrors.Empty(nameof(DeviceName)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<DeviceName>(ValueObjectsErrors.TooLong(nameof(DeviceName), MaxLength));
        }

        return new DeviceName(trimmed);
    }

    internal static DeviceName FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
