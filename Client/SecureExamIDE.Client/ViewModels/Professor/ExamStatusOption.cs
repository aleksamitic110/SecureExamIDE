using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One choice in the status filter. "All" is the absence of a filter rather than a fourth status,
// which is why Value is nullable.
public sealed record ExamStatusOption(string Label, ExamStatus? Value)
{
    public static readonly ExamStatusOption All = new("All", null);

    public static readonly IReadOnlyList<ExamStatusOption> Options =
    [
        All,
        new("Drafts", ExamStatus.Draft),
        new("Published", ExamStatus.Published),
        new("Archived", ExamStatus.Archived)
    ];

    public override string ToString() => Label;
}
