using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Exams;

public sealed record ExamDescription
{
    public const int MaxLength = 2000;

    private ExamDescription(string value) => Value = value;

    public string Value { get; }

    public static Result<ExamDescription> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ExamDescription>(ValueObjectsErrors.Empty(nameof(ExamDescription)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<ExamDescription>(
                ValueObjectsErrors.TooLong(nameof(ExamDescription), MaxLength));
        }

        return new ExamDescription(trimmed);
    }

    internal static ExamDescription FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
