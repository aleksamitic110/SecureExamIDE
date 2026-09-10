using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class CreateExamDependencyUploadUrlHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc);

    private static CreateExamDependencyUploadUrl.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new CreateExamDependencyUploadUrl.Handler(
            context, userContext, storageService, dateTimeProvider);
    }

    private static IStorageService SigningStorage()
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService
            .CreatePresignedUploadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => $"https://storage.test/{callInfo.Arg<string>()}?signature=abc");

        return storageService;
    }

    // The key is minted by the server and the signature is bound to it, which is what stops a
    // caller writing into another exam's prefix.
    [Fact]
    public async Task Handle_Should_SignAUrlForAServerMintedKeyUnderTheDependencyPrefix()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);

        CreateExamDependencyUploadUrl.Handler handler = CreateHandler(context, SigningStorage());

        // Act
        Result<CreateExamDependencyUploadUrl.Response> result = await handler.Handle(
            new CreateExamDependencyUploadUrl.Command(examId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ObjectKey.ShouldStartWith($"exams/{examId}/dependencies/");
        result.Value.UploadUrl.ShouldContain(result.Value.ObjectKey);
        result.Value.ExpiresAt.ShouldBeGreaterThan(Now);
    }

    [Fact]
    public async Task Handle_Should_MintADifferentKeyEachTime()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);

        CreateExamDependencyUploadUrl.Handler handler = CreateHandler(context, SigningStorage());

        // Act
        Result<CreateExamDependencyUploadUrl.Response> first = await handler.Handle(
            new CreateExamDependencyUploadUrl.Command(examId), CancellationToken.None);
        Result<CreateExamDependencyUploadUrl.Response> second = await handler.Handle(
            new CreateExamDependencyUploadUrl.Command(examId), CancellationToken.None);

        // Assert
        first.Value.ObjectKey.ShouldNotBe(second.Value.ObjectKey);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ownerId: Guid.NewGuid());

        IStorageService storageService = SigningStorage();
        CreateExamDependencyUploadUrl.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<CreateExamDependencyUploadUrl.Response> result = await handler.Handle(
            new CreateExamDependencyUploadUrl.Command(examId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(examId));

        // No URL is signed for an exam the caller does not own.
        await storageService.DidNotReceiveWithAnyArgs()
            .CreatePresignedUploadUrlAsync(default!, default, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenTheExamIsNoLongerADraft()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);

        IStorageService storageService = SigningStorage();
        CreateExamDependencyUploadUrl.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<CreateExamDependencyUploadUrl.Response> result = await handler.Handle(
            new CreateExamDependencyUploadUrl.Command(examId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotDraft(ExamPackageStatus.Published));

        await storageService.DidNotReceiveWithAnyArgs()
            .CreatePresignedUploadUrlAsync(default!, default, default);
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
            CreatedAt = Now
        };

        context.ExamPackages.Add(exam);
        await context.SaveChangesAsync();

        return exam.Id;
    }
}
