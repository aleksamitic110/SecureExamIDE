using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class RemoveExamFileHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();

    private static RemoveExamFile.Handler CreateHandler(ApplicationDbContext context, IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new RemoveExamFile.Handler(context, userContext, storageService);
    }

    [Fact]
    public async Task Handle_Should_RemoveTheRowAndDeleteTheObject()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);
        ExamFile file = await ExamSeed.SeedFileAsync(context, exam.Id);
        string objectKey = file.ObjectKey.Value;

        IStorageService storageService = Substitute.For<IStorageService>();
        RemoveExamFile.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(new RemoveExamFile.Command(exam.Id, file.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await context.ExamFiles.CountAsync()).ShouldBe(0);
        await storageService.Received(1).DeleteAsync(objectKey, Arg.Any<CancellationToken>());
    }

    // A published exam's files may already be sealed into a sitting's package - they stay.
    [Fact]
    public async Task Handle_Should_TouchNothing_WhenTheExamIsPublished()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);
        ExamFile file = await ExamSeed.SeedFileAsync(context, exam.Id);

        IStorageService storageService = Substitute.For<IStorageService>();
        RemoveExamFile.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(new RemoveExamFile.Command(exam.Id, file.Id), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotDraft(ExamPackageStatus.Published));
        (await context.ExamFiles.CountAsync()).ShouldBe(1);
        await storageService.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    // The file is looked up within the exam in the route, so another exam's file id is not found
    // here even when the caller owns both exams.
    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheFileBelongsToAnotherExam()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);
        ExamPackage otherExam = await ExamSeed.SeedExamAsync(context, ProfessorId);
        ExamFile otherFile = await ExamSeed.SeedFileAsync(context, otherExam.Id);

        IStorageService storageService = Substitute.For<IStorageService>();
        RemoveExamFile.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(new RemoveExamFile.Command(exam.Id, otherFile.Id), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.FileNotFound(otherFile.Id));
        (await context.ExamFiles.CountAsync()).ShouldBe(1);
        await storageService.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, Guid.NewGuid());
        ExamFile file = await ExamSeed.SeedFileAsync(context, exam.Id);

        RemoveExamFile.Handler handler = CreateHandler(context, Substitute.For<IStorageService>());

        // Act
        Result result = await handler.Handle(new RemoveExamFile.Command(exam.Id, file.Id), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(exam.Id));
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenTheFileIsRemoved()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);
        ExamFile file = await ExamSeed.SeedFileAsync(context, exam.Id);

        RemoveExamFile.Handler handler = CreateHandler(context, Substitute.For<IStorageService>());

        // Act
        await handler.Handle(new RemoveExamFile.Command(exam.Id, file.Id), CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is ExamFileRemovedDomainEvent)),
            Arg.Any<CancellationToken>());
    }
}
