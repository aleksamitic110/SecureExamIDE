using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

// Builds an exam and the dependencies attached to it, for the tests of the student-facing
// dependency endpoints.
internal static class DependencySeed
{
    public static async Task<Guid> SeedExamAsync(
        ApplicationDbContext context,
        ExamPackageStatus status = ExamPackageStatus.Published)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Compilers".AsExamTitle(),
            Description = "Final".AsExamDescription(),
            Subject = "Compiler Construction".AsExamSubject(),
            OwnerProfessorId = Guid.NewGuid(),
            Status = status,
            CreatedAt = CreatedAt,
            PublishedAt = status == ExamPackageStatus.Published ? CreatedAt.AddDays(1) : null
        };

        context.ExamPackages.Add(exam);
        await context.SaveChangesAsync();

        return exam.Id;
    }

    public static async Task<ExamDependency> SeedDependencyAsync(
        ApplicationDbContext context,
        Guid examId,
        string name,
        string version,
        long sizeBytes,
        DateTime createdAt)
    {
        var dependency = new ExamDependency
        {
            Id = Guid.NewGuid(),
            ExamPackageId = examId,
            Name = name.AsDependencyName(),
            Version = version.AsDependencyVersion(),
            ContentType = "application/zip".AsContentType(),
            ObjectKey = ExamObjectKeys.NewDependencyKey(examId),
            SizeBytes = sizeBytes,
            CreatedAt = createdAt
        };

        context.ExamDependencies.Add(dependency);
        await context.SaveChangesAsync();

        return dependency;
    }

    public static readonly DateTime CreatedAt = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
}
