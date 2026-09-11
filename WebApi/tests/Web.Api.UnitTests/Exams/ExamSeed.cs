using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

// Builds an exam owned by a given professor and the task files in it, for the tests of the
// slices that correct a draft. Dependencies come from DependencySeed.
internal static class ExamSeed
{
    public static async Task<ExamPackage> SeedExamAsync(
        ApplicationDbContext context,
        Guid ownerId,
        ExamPackageStatus status = ExamPackageStatus.Draft)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Compilers".AsExamTitle(),
            Description = "Write a parser.".AsExamDescription(),
            Subject = "Compiler Construction".AsExamSubject(),
            OwnerProfessorId = ownerId,
            Status = status,
            CreatedAt = CreatedAt,
            PublishedAt = status == ExamPackageStatus.Published ? CreatedAt.AddDays(1) : null
        };

        context.ExamPackages.Add(exam);
        await context.SaveChangesAsync();

        return exam;
    }

    public static async Task<ExamFile> SeedFileAsync(ApplicationDbContext context, Guid examId)
    {
        var file = new ExamFile
        {
            Id = Guid.NewGuid(),
            ExamPackageId = examId,
            FileName = "task.pdf".AsFileName(),
            ContentType = "application/pdf".AsContentType(),
            ObjectKey = ExamObjectKeys.NewFileKey(examId),
            SizeBytes = 1_024,
            Sha256 = new string('a', 64).AsSha256(),
            CreatedAt = CreatedAt
        };

        context.ExamFiles.Add(file);
        await context.SaveChangesAsync();

        return file;
    }

    public static readonly DateTime CreatedAt = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
}
