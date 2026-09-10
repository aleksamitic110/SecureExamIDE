using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Exams;

// The human-readable name of a tool a student needs in order to work on the exam offline - a
// compiler, an SDK, a library archive.
public sealed record DependencyName
{
    public const int MaxLength = 200;

    private DependencyName(string value) => Value = value;

    public string Value { get; }

    public static Result<DependencyName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<DependencyName>(ValueObjectsErrors.Empty(nameof(DependencyName)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<DependencyName>(ValueObjectsErrors.TooLong(nameof(DependencyName), MaxLength));
        }

        return new DependencyName(trimmed);
    }

    internal static DependencyName FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
