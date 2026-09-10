using Web.Api.Common;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.Features.Users;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class GetPublishedExamsHandlerTests : BaseHandlerTest
{
    private const string Sha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private static readonly DateTime Noon = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_Should_ReturnOnlyPublishedExams()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid professorId = await SeedProfessorAsync(context);
        await SeedExamAsync(context, professorId, "Published one", ExamPackageStatus.Published, Noon);
        await SeedExamAsync(context, professorId, "Still a draft", ExamPackageStatus.Draft);
        await SeedExamAsync(context, professorId, "Archived", ExamPackageStatus.Archived, Noon);

        var handler = new GetPublishedExams.Handler(context);

        // Act
        Result<PagedList<GetPublishedExams.Response>> result = await handler.Handle(
            new GetPublishedExams.Query(Page: 1, PageSize: 20, Subject: null),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().Title.ShouldBe("Published one");
    }

    [Fact]
    public async Task Handle_Should_ReturnTheNewestFirst()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid professorId = await SeedProfessorAsync(context);
        await SeedExamAsync(context, professorId, "Older", ExamPackageStatus.Published, Noon.AddDays(-2));
        await SeedExamAsync(context, professorId, "Newest", ExamPackageStatus.Published, Noon);
        await SeedExamAsync(context, professorId, "Middle", ExamPackageStatus.Published, Noon.AddDays(-1));

        var handler = new GetPublishedExams.Handler(context);

        // Act
        Result<PagedList<GetPublishedExams.Response>> result = await handler.Handle(
            new GetPublishedExams.Query(Page: 1, PageSize: 20, Subject: null),
            CancellationToken.None);

        // Assert
        result.Value.Items.Select(e => e.Title).ShouldBe(["Newest", "Middle", "Older"]);
    }

    [Fact]
    public async Task Handle_Should_ReturnTheRequestedPageWithoutRepeatingRows()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid professorId = await SeedProfessorAsync(context);

        for (int i = 0; i < 25; i++)
        {
            await SeedExamAsync(
                context,
                professorId,
                $"Exam {i:00}",
                ExamPackageStatus.Published,
                Noon.AddMinutes(-i));
        }

        var handler = new GetPublishedExams.Handler(context);

        // Act
        Result<PagedList<GetPublishedExams.Response>> first = await handler.Handle(
            new GetPublishedExams.Query(Page: 1, PageSize: 10, Subject: null),
            CancellationToken.None);

        Result<PagedList<GetPublishedExams.Response>> second = await handler.Handle(
            new GetPublishedExams.Query(Page: 2, PageSize: 10, Subject: null),
            CancellationToken.None);

        Result<PagedList<GetPublishedExams.Response>> third = await handler.Handle(
            new GetPublishedExams.Query(Page: 3, PageSize: 10, Subject: null),
            CancellationToken.None);

        // Assert
        first.Value.Items.Count.ShouldBe(10);
        second.Value.Items.Count.ShouldBe(10);
        third.Value.Items.Count.ShouldBe(5);
        first.Value.TotalCount.ShouldBe(25);
        third.Value.HasNextPage.ShouldBeFalse();

        IEnumerable<Guid> everything =
        [
            .. first.Value.Items.Select(e => e.Id),
            .. second.Value.Items.Select(e => e.Id),
            .. third.Value.Items.Select(e => e.Id)
        ];

        everything.Distinct().Count().ShouldBe(25);
    }

    [Fact]
    public async Task Handle_Should_CapThePageSize()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid professorId = await SeedProfessorAsync(context);
        await SeedExamAsync(context, professorId, "Only one", ExamPackageStatus.Published, Noon);

        var handler = new GetPublishedExams.Handler(context);

        // Act
        Result<PagedList<GetPublishedExams.Response>> result = await handler.Handle(
            new GetPublishedExams.Query(Page: 1, PageSize: 100_000, Subject: null),
            CancellationToken.None);

        // Assert
        result.Value.PageSize.ShouldBe(PagedList<GetPublishedExams.Response>.MaxPageSize);
    }

    [Fact]
    public async Task Handle_Should_FilterBySubject()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid professorId = await SeedProfessorAsync(context);
        await SeedExamAsync(context, professorId, "Trees", ExamPackageStatus.Published, Noon, "Algorithms");
        await SeedExamAsync(context, professorId, "Parsers", ExamPackageStatus.Published, Noon, "Compilers");

        var handler = new GetPublishedExams.Handler(context);

        // Act
        Result<PagedList<GetPublishedExams.Response>> result = await handler.Handle(
            new GetPublishedExams.Query(Page: 1, PageSize: 20, Subject: "Compilers"),
            CancellationToken.None);

        // Assert
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().Title.ShouldBe("Parsers");
    }

    // A filter the server cannot make sense of must not silently become "no filter", or a caller
    // would get the whole catalog back believing it was narrowed.
    [Fact]
    public async Task Handle_Should_ReturnNothing_WhenTheSubjectFilterIsMalformed()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid professorId = await SeedProfessorAsync(context);
        await SeedExamAsync(context, professorId, "Trees", ExamPackageStatus.Published, Noon);

        var handler = new GetPublishedExams.Handler(context);

        // Act
        Result<PagedList<GetPublishedExams.Response>> result = await handler.Handle(
            new GetPublishedExams.Query(Page: 1, PageSize: 20, Subject: new string('x', 500)),
            CancellationToken.None);

        // Assert
        result.Value.Items.ShouldBeEmpty();
        result.Value.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_ReportTheDownloadSizeAndCounts()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid professorId = await SeedProfessorAsync(context);
        Guid examId = await SeedExamAsync(context, professorId, "Trees", ExamPackageStatus.Published, Noon);

        context.ExamFiles.Add(new ExamFile
        {
            Id = Guid.NewGuid(),
            ExamPackageId = examId,
            FileName = "task.pdf".AsFileName(),
            ContentType = "application/pdf".AsContentType(),
            ObjectKey = ExamObjectKeys.NewFileKey(examId),
            SizeBytes = 1_000,
            Sha256 = Sha256.AsSha256(),
            CreatedAt = Noon
        });

        context.ExamDependencies.Add(new ExamDependency
        {
            Id = Guid.NewGuid(),
            ExamPackageId = examId,
            Name = "GCC".AsDependencyName(),
            Version = "13.2.0".AsDependencyVersion(),
            ContentType = "application/gzip".AsContentType(),
            ObjectKey = ExamObjectKeys.NewDependencyKey(examId),
            SizeBytes = 40_000,
            CreatedAt = Noon
        });

        await context.SaveChangesAsync();

        var handler = new GetPublishedExams.Handler(context);

        // Act
        Result<PagedList<GetPublishedExams.Response>> result = await handler.Handle(
            new GetPublishedExams.Query(Page: 1, PageSize: 20, Subject: null),
            CancellationToken.None);

        // Assert
        GetPublishedExams.Response exam = result.Value.Items.Single();
        exam.FileCount.ShouldBe(1);
        exam.DependencyCount.ShouldBe(1);
        exam.TotalSizeBytes.ShouldBe(41_000);
        exam.ProfessorFirstName.ShouldBe("Ana");
        exam.ProfessorLastName.ShouldBe("Petrovic");
    }

    [Fact]
    public async Task Handle_Should_ReportZeroSize_WhenAnExamHasNothingAttached()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid professorId = await SeedProfessorAsync(context);
        await SeedExamAsync(context, professorId, "Empty", ExamPackageStatus.Published, Noon);

        var handler = new GetPublishedExams.Handler(context);

        // Act
        Result<PagedList<GetPublishedExams.Response>> result = await handler.Handle(
            new GetPublishedExams.Query(Page: 1, PageSize: 20, Subject: null),
            CancellationToken.None);

        // Assert
        result.Value.Items.Single().TotalSizeBytes.ShouldBe(0);
    }

    private static async Task<Guid> SeedProfessorAsync(ApplicationDbContext context)
    {
        var professor = new User
        {
            Id = Guid.NewGuid(),
            Email = "ana@example.com".AsEmail(),
            FirstName = "Ana".AsPersonName(),
            LastName = "Petrovic".AsPersonName(),
            PasswordHash = "hash",
            Role = Role.Professor,
            IsActive = true
        };

        context.Users.Add(professor);
        await context.SaveChangesAsync();

        return professor.Id;
    }

    private static async Task<Guid> SeedExamAsync(
        ApplicationDbContext context,
        Guid ownerId,
        string title,
        ExamPackageStatus status,
        DateTime? publishedAt = null,
        string subject = "Algorithms and Data Structures")
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = title.AsExamTitle(),
            Description = "Description".AsExamDescription(),
            Subject = subject.AsExamSubject(),
            OwnerProfessorId = ownerId,
            Status = status,
            CreatedAt = Noon,
            PublishedAt = publishedAt
        };

        context.ExamPackages.Add(exam);
        await context.SaveChangesAsync();

        return exam.Id;
    }
}
