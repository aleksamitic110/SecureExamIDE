using System.Text.RegularExpressions;
using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

// A student's faculty index number, such as 19252. Four to six digits; professors have none.
public sealed partial record IndexNumber
{
    public const int MaxLength = 6;

    private IndexNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<IndexNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<IndexNumber>(ValueObjectsErrors.Empty(nameof(IndexNumber)));
        }

        string trimmed = value.Trim();

        if (!Pattern().IsMatch(trimmed))
        {
            return Result.Failure<IndexNumber>(ValueObjectsErrors.InvalidFormat(nameof(IndexNumber)));
        }

        return new IndexNumber(trimmed);
    }

    internal static IndexNumber FromTrusted(string value) => new(value);

    public override string ToString() => Value;

    [GeneratedRegex(@"^\d{4,6}$")]
    private static partial Regex Pattern();
}
