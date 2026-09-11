using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Exams;

public sealed record ExamSubject
{
    public const int MaxLength = 200;

    private ExamSubject(string value) => Value = value;

    public string Value { get; }

    public static Result<ExamSubject> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ExamSubject>(ValueObjectsErrors.Empty(nameof(ExamSubject)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<ExamSubject>(ValueObjectsErrors.TooLong(nameof(ExamSubject), MaxLength));
        }

        return new ExamSubject(trimmed);
    }

    internal static ExamSubject FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
