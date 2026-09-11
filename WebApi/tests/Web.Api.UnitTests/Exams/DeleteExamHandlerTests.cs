using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class DeleteExamHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();

    private static DeleteExam.Handler CreateHandler(ApplicationDbContext context, IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new DeleteExam.Handler(context, userContext, storageService);
    }

    [Fact]
    public async Task Handle_Should_DeleteTheDraftWithItsFilesDependenciesAndObjects()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);
        ExamFile file = await ExamSeed.SeedFileAsync(context, exam.Id);
        ExamDependency dependency = await DependencySeed.SeedDependencyAsync(
            context, exam.Id, "GCC", "14.2.0", 95_000_000, ExamSeed.CreatedAt);

        string fileKey = file.ObjectKey.Value;
        string dependencyKey = dependency.ObjectKey.Value;

        IStorageService storageService = Substitute.For<IStorageService>();
        DeleteExam.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(new DeleteExam.Command(exam.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await context.ExamPackages.CountAsync()).ShouldBe(0);
        (await context.ExamFiles.CountAsync()).ShouldBe(0);
        (await context.ExamDependencies.CountAsync()).ShouldBe(0);

        await storageService.Received(1).DeleteAsync(fileKey, Arg.Any<CancellationToken>());
        await storageService.Received(1).DeleteAsync(dependencyKey, Arg.Any<CancellationToken>());
    }

    // A published exam is never deleted: sittings, sealed packages and submissions hang off it.
    [Fact]
    public async Task Handle_Should_TouchNothing_WhenTheExamIsPublished()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);
        await ExamSeed.SeedFileAsync(context, exam.Id);

        IStorageService storageService = Substitute.For<IStorageService>();
        DeleteExam.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(new DeleteExam.Command(exam.Id), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotDraft(ExamPackageStatus.Published));
        (await context.ExamPackages.CountAsync()).ShouldBe(1);
        (await context.ExamFiles.CountAsync()).ShouldBe(1);
        await storageService.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, Guid.NewGuid());

        IStorageService storageService = Substitute.For<IStorageService>();
        DeleteExam.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(new DeleteExam.Command(exam.Id), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(exam.Id));
        (await context.ExamPackages.CountAsync()).ShouldBe(1);
        await storageService.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenTheDraftIsDeleted()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);

        DeleteExam.Handler handler = CreateHandler(context, Substitute.For<IStorageService>());

        // Act
        await handler.Handle(new DeleteExam.Command(exam.Id), CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is ExamPackageDeletedDomainEvent)),
            Arg.Any<CancellationToken>());
    }
}
