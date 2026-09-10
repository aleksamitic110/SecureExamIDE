using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Exams;

// Lives with the Exams feature rather than in Common/ValueObjects, because only this feature gives
// it meaning - Common must never need to know about Features.
public sealed record ExamTitle
{
    public const int MaxLength = 200;

    private ExamTitle(string value) => Value = value;

    public string Value { get; }

    public static Result<ExamTitle> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ExamTitle>(ValueObjectsErrors.Empty(nameof(ExamTitle)));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<ExamTitle>(ValueObjectsErrors.TooLong(nameof(ExamTitle), MaxLength));
        }

        return new ExamTitle(trimmed);
    }

    internal static ExamTitle FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
