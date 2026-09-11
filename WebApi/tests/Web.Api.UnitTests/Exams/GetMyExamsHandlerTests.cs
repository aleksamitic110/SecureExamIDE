using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class GetMyExamsHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Digest = new('a', 64);

    private static GetMyExams.Handler CreateHandler(ApplicationDbContext context)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new GetMyExams.Handler(context, userContext);
    }

    private static GetMyExams.Query QueryFor(ExamPackageStatus? status = null) => new(1, 20, status);

    [Fact]
    public async Task Handle_Should_ListOnlyTheCallersExams_DraftsIncluded_NewestFirst()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid published = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published, Now.AddDays(-5));
        Guid draft = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Draft, Now.AddDays(-1));
        await SeedExamAsync(context, Guid.NewGuid(), ExamPackageStatus.Published, Now);

        GetMyExams.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetMyExams.Response>> result = await handler.Handle(QueryFor(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(e => e.Id).ShouldBe([draft, published]);
        result.Value.Items.Select(e => e.Status).ShouldBe(["Draft", "Published"]);
    }

    [Fact]
    public async Task Handle_Should_ReturnOnlyTheRequestedStatus()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid draft = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Draft, Now.AddDays(-1));
        await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published, Now.AddDays(-2));

        GetMyExams.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetMyExams.Response>> result = await handler.Handle(
            QueryFor(ExamPackageStatus.Draft),
            CancellationToken.None);

        // Assert
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().Id.ShouldBe(draft);
    }

    // Cancelled sittings are still part of the exam's history, so they are counted.
    [Fact]
    public async Task Handle_Should_CountFilesDependenciesAndSittings()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published, Now);

        await SeedFileAsync(context, examId);
        await SeedFileAsync(context, examId);
        await DependencySeed.SeedDependencyAsync(context, examId, "GCC", "14.2.0", 1_000, Now);
        await SeedSessionAsync(context, examId, isActive: true);
        await SeedSessionAsync(context, examId, isActive: false);

        GetMyExams.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetMyExams.Response>> result = await handler.Handle(QueryFor(), CancellationToken.None);

        // Assert
        GetMyExams.Response exam = result.Value.Items.Single();
        exam.FileCount.ShouldBe(2);
        exam.DependencyCount.ShouldBe(1);
        exam.SessionCount.ShouldBe(2);
    }

    private static async Task<Guid> SeedExamAsync(
        ApplicationDbContext context,
        Guid ownerId,
        ExamPackageStatus status,
        DateTime createdAt)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Compilers".AsExamTitle(),
            Description = "Final".AsExamDescription(),
            Subject = "Compiler Construction".AsExamSubject(),
            OwnerProfessorId = ownerId,
            Status = status,
            CreatedAt = createdAt,
            PublishedAt = status == ExamPackageStatus.Published ? createdAt.AddHours(1) : null
        };

        context.ExamPackages.Add(exam);
        await context.SaveChangesAsync();

        return exam.Id;
    }

    private static async Task SeedFileAsync(ApplicationDbContext context, Guid examId)
    {
        context.ExamFiles.Add(new ExamFile
        {
            Id = Guid.NewGuid(),
            ExamPackageId = examId,
            FileName = "task.txt".AsFileName(),
            ContentType = "text/plain".AsContentType(),
            ObjectKey = ExamObjectKeys.NewFileKey(examId),
            SizeBytes = 10,
            Sha256 = Digest.AsSha256(),
            CreatedAt = Now
        });

        await context.SaveChangesAsync();
    }

    private static async Task SeedSessionAsync(ApplicationDbContext context, Guid examId, bool isActive)
    {
        context.ExamSessions.Add(new ExamSession
        {
            Id = Guid.NewGuid(),
            ExamPackageId = examId,
            CreatedByProfessorId = ProfessorId,
            StartsAt = Now.AddDays(1),
            EndsAt = Now.AddDays(1).AddHours(3),
            IsActive = isActive,
            OneTimeCodeHash = Digest.AsSha256(),
            PackageObjectKey = $"exams/{examId}/sessions/{Guid.NewGuid()}/package.bin".AsObjectKey(),
            HeaderObjectKey = $"exams/{examId}/sessions/{Guid.NewGuid()}/package.hdr".AsObjectKey(),
            PackageSizeBytes = 100,
            PackageSha256 = Digest.AsSha256(),
            CreatedAt = Now
        });

        await context.SaveChangesAsync();
    }
}
