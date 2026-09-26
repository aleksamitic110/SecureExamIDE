using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.ViewModels.Home;

// One exam in the catalog list, with the text it is shown with.
public sealed class CatalogExamItem(CatalogExam exam, bool hasDownloadedSitting)
{
    public CatalogExam Exam => exam;

    public string Title => exam.Title;

    public string Subject => exam.Subject;

    public string Description => exam.Description;

    public string ProfessorName => $"{exam.ProfessorFirstName} {exam.ProfessorLastName}";

    public string Contents =>
        $"{Plural.Format(exam.FileCount, "task file")} · {Plural.Format(exam.DependencyCount, "dependency", "dependencies")} · {ByteSize.Format(exam.TotalSizeBytes)}";

    public bool HasDownloadedSitting => hasDownloadedSitting;
}
