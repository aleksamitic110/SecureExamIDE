using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class PublishExamHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private static readonly DateTime PublishedAt = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private const string Sha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static PublishExam.Handler CreateHandler(ApplicationDbContext context)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(PublishedAt);

        return new PublishExam.Handler(context, userContext, dateTimeProvider);
    }

    [Fact]
    public async Task Handle_Should_PublishAndStampTheTime_WhenExamHasFiles()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        await SeedFileAsync(context, examId);

        PublishExam.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new PublishExam.Command(examId), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        ExamPackage exam = await context.ExamPackages.SingleAsync();
        exam.Status.ShouldBe(ExamPackageStatus.Published);
        exam.PublishedAt.ShouldBe(PublishedAt);
    }

    // An exam with nothing to download would be visible in the catalog and useless to open, so the
    // empty case is refused rather than published.
    [Fact]
    public async Task Handle_Should_LeaveExamAsDraft_WhenItHasNoFiles()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);

        PublishExam.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new PublishExam.Command(examId), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NoFiles);

        ExamPackage exam = await context.ExamPackages.SingleAsync();
        exam.Status.ShouldBe(ExamPackageStatus.Draft);
        exam.PublishedAt.ShouldBeNull();
    }

    // Dependencies are optional: an exam needing no toolchain beyond what the student already has
    // is still publishable.
    [Fact]
    public async Task Handle_Should_Publish_WhenExamHasNoDependencies()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        await SeedFileAsync(context, examId);

        PublishExam.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new PublishExam.Command(examId), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await context.ExamDependencies.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenExamIsAlreadyPublished()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);
        await SeedFileAsync(context, examId);

        PublishExam.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new PublishExam.Command(examId), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.CannotPublish(ExamPackageStatus.Published));
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ownerId: Guid.NewGuid());
        await SeedFileAsync(context, examId);

        PublishExam.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new PublishExam.Command(examId), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(examId));

        ExamPackage exam = await context.ExamPackages.SingleAsync();
        exam.Status.ShouldBe(ExamPackageStatus.Draft);
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenExamIsPublished()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        Guid examId = await SeedExamAsync(context, ProfessorId);
        await SeedFileAsync(context, examId);

        PublishExam.Handler handler = CreateHandler(context);

        // Act
        await handler.Handle(new PublishExam.Command(examId), CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is ExamPackagePublishedDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    private static async Task<Guid> SeedExamAsync(
        ApplicationDbContext context,
        Guid ownerId,
        ExamPackageStatus status = ExamPackageStatus.Draft)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Algorithms".AsExamTitle(),
            Description = "Final exam".AsExamDescription(),
            Subject = "Algorithms and Data Structures".AsExamSubject(),
            OwnerProfessorId = ownerId,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

        context.ExamPackages.Add(exam);
        await context.SaveChangesAsync();

        return exam.Id;
    }

    private static async Task SeedFileAsync(ApplicationDbContext context, Guid examId)
    {
        var file = new ExamFile
        {
            Id = Guid.NewGuid(),
            ExamPackageId = examId,
            FileName = "task.pdf".AsFileName(),
            ContentType = "application/pdf".AsContentType(),
            ObjectKey = ExamObjectKeys.NewFileKey(examId),
            SizeBytes = 3,
            Sha256 = Sha256.AsSha256(),
            CreatedAt = DateTime.UtcNow
        };

        context.ExamFiles.Add(file);
        await context.SaveChangesAsync();
    }
}
