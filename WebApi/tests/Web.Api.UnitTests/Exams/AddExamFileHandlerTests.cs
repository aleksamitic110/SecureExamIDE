using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class AddExamFileHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private const string Sha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static AddExamFile.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(DateTime.UtcNow);

        return new AddExamFile.Handler(context, userContext, storageService, dateTimeProvider);
    }

    private static IStorageService StorageHolding(string objectKey, long sizeBytes = 3) =>
        StorageHolding(objectKey, sizeBytes, "text/plain");

    private static IStorageService StorageHolding(string objectKey, long sizeBytes, string contentType)
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService.StatAsync(objectKey, Arg.Any<CancellationToken>())
            .Returns(new StorageObjectInfo(objectKey, sizeBytes, contentType));

        return storageService;
    }

    private static IStorageService EmptyStorage()
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService.StatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((StorageObjectInfo?)null);

        return storageService;
    }

    // The check the whole two-phase split exists for: nothing is written to the database unless the
    // bytes are demonstrably already in storage.
    [Fact]
    public async Task Handle_Should_WriteNoRow_WhenTheObjectWasNeverUploaded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/files/{Guid.NewGuid()}";

        AddExamFile.Handler handler = CreateHandler(context, EmptyStorage());

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamFile.Command(examId, objectKey, "task.pdf", Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.ContentNotUploaded);
        (await context.ExamFiles.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_ReturnForbidden_WhenObjectKeyBelongsToAnotherExam()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string otherExamKey = $"exams/{Guid.NewGuid()}/files/{Guid.NewGuid()}";

        IStorageService storageService = StorageHolding(otherExamKey);
        AddExamFile.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamFile.Command(examId, otherExamKey, "stolen.pdf", Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.ObjectKeyNotOwnedByExam);
        (await context.ExamFiles.CountAsync()).ShouldBe(0);

        // The ownership guard runs before storage is consulted at all.
        await storageService.DidNotReceiveWithAnyArgs().StatAsync(default!, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ownerId: Guid.NewGuid());
        string objectKey = $"exams/{examId}/files/{Guid.NewGuid()}";

        AddExamFile.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamFile.Command(examId, objectKey, "task.pdf", Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(examId));
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenTheSameObjectIsCommittedTwice()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/files/{Guid.NewGuid()}";

        AddExamFile.Handler handler = CreateHandler(context, StorageHolding(objectKey));
        var command = new AddExamFile.Command(examId, objectKey, "task.pdf", Sha256);

        await handler.Handle(command, CancellationToken.None);

        // Act
        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.ContentAlreadyCommitted);
        (await context.ExamFiles.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_TakeSizeAndContentTypeFromStorageRatherThanTheRequest()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/files/{Guid.NewGuid()}";

        AddExamFile.Handler handler = CreateHandler(
            context,
            StorageHolding(objectKey, sizeBytes: 4096, contentType: "application/pdf"));

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamFile.Command(examId, objectKey, "task.pdf", Sha256),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        ExamFile file = await context.ExamFiles.SingleAsync();
        file.SizeBytes.ShouldBe(4096);
        file.ContentType!.Value.ShouldBe("application/pdf");
        file.ObjectKey!.Value.ShouldBe(objectKey);
        file.FileName!.Value.ShouldBe("task.pdf");
        file.Sha256!.Value.ShouldBe(Sha256);
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenFileIsRecorded()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/files/{Guid.NewGuid()}";

        AddExamFile.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        await handler.Handle(
            new AddExamFile.Command(examId, objectKey, "task.pdf", Sha256),
            CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is ExamFileAddedDomainEvent)),
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
}
