using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class RemoveExamDependencyHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();

    private static RemoveExamDependency.Handler CreateHandler(ApplicationDbContext context, IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new RemoveExamDependency.Handler(context, userContext, storageService);
    }

    private static async Task<ExamDependency> SeedDependencyAsync(ApplicationDbContext context, Guid examId) =>
        await DependencySeed.SeedDependencyAsync(context, examId, "GCC", "14.2.0", 95_000_000, ExamSeed.CreatedAt);

    [Fact]
    public async Task Handle_Should_RemoveTheRowAndDeleteTheObject()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);
        ExamDependency dependency = await SeedDependencyAsync(context, exam.Id);
        string objectKey = dependency.ObjectKey.Value;

        IStorageService storageService = Substitute.For<IStorageService>();
        RemoveExamDependency.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(
            new RemoveExamDependency.Command(exam.Id, dependency.Id),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await context.ExamDependencies.CountAsync()).ShouldBe(0);
        await storageService.Received(1).DeleteAsync(objectKey, Arg.Any<CancellationToken>());
    }

    // Students may already have downloaded a published exam's toolchain - it stays.
    [Fact]
    public async Task Handle_Should_TouchNothing_WhenTheExamIsPublished()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);
        ExamDependency dependency = await SeedDependencyAsync(context, exam.Id);

        IStorageService storageService = Substitute.For<IStorageService>();
        RemoveExamDependency.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(
            new RemoveExamDependency.Command(exam.Id, dependency.Id),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotDraft(ExamPackageStatus.Published));
        (await context.ExamDependencies.CountAsync()).ShouldBe(1);
        await storageService.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheDependencyBelongsToAnotherExam()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);
        ExamPackage otherExam = await ExamSeed.SeedExamAsync(context, ProfessorId);
        ExamDependency otherDependency = await SeedDependencyAsync(context, otherExam.Id);

        IStorageService storageService = Substitute.For<IStorageService>();
        RemoveExamDependency.Handler handler = CreateHandler(context, storageService);

        // Act
        Result result = await handler.Handle(
            new RemoveExamDependency.Command(exam.Id, otherDependency.Id),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.DependencyNotFound(otherDependency.Id));
        await storageService.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, Guid.NewGuid());
        ExamDependency dependency = await SeedDependencyAsync(context, exam.Id);

        RemoveExamDependency.Handler handler = CreateHandler(context, Substitute.For<IStorageService>());

        // Act
        Result result = await handler.Handle(
            new RemoveExamDependency.Command(exam.Id, dependency.Id),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(exam.Id));
    }
}
