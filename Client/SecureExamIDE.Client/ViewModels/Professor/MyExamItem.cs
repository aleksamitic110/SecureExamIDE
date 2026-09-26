using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One of the professor's exams in the list, with the text it is shown with.
public sealed class MyExamItem(MyExam exam)
{
    public MyExam Exam => exam;

    public Guid Id => exam.Id;

    public string Title => exam.Title;

    public string Subject => exam.Subject;

    public string Status => exam.Status.ToString();

    // Only a draft can still be corrected or thrown away; everything else is read-only for good.
    public bool IsDraft => exam.Status == ExamStatus.Draft;

    public string Contents =>
        $"{Plural.Format(exam.FileCount, "task file")} · " +
        $"{Plural.Format(exam.DependencyCount, "dependency", "dependencies")} · " +
        $"{Plural.Format(exam.SessionCount, "sitting")}";

    // Once an exam is published, when that happened is the date that matters.
    public string When => exam.PublishedAt is { } publishedAt
        ? $"Published {publishedAt.ToLocalTime():d MMM yyyy HH:mm}"
        : $"Created {exam.CreatedAt.ToLocalTime():d MMM yyyy HH:mm}";
}
