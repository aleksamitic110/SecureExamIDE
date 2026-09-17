using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class AddExamDependencyHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();

    private static AddExamDependency.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService,
        long maxDependencyBytes = 0)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(DateTime.UtcNow);

        return new AddExamDependency.Handler(
            context,
            userContext,
            storageService,
            dateTimeProvider,
            Options.Create(new ExamOptions { MaxDependencyBytes = maxDependencyBytes }));
    }

    private static IStorageService StorageHolding(
        string objectKey,
        long sizeBytes = 3,
        string contentType = "application/zip")
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

    [Fact]
    public async Task Handle_Should_WriteNoRow_WhenTheObjectWasNeverUploaded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, EmptyStorage());

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, objectKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.ContentNotUploaded);
        (await context.ExamDependencies.CountAsync()).ShouldBe(0);
    }

    // The reason files and dependencies get separate key prefixes: an uploaded exam task must not
    // be committable as a dependency, which is content the student may open before the exam.
    [Fact]
    public async Task Handle_Should_ReturnForbidden_WhenObjectKeyIsAnExamFileKey()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string fileKey = $"exams/{examId}/files/{Guid.NewGuid()}";

        IStorageService storageService = StorageHolding(fileKey);
        AddExamDependency.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, fileKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.ObjectKeyNotOwnedByExam);
        (await context.ExamDependencies.CountAsync()).ShouldBe(0);
        await storageService.DidNotReceiveWithAnyArgs().StatAsync(default!, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnForbidden_WhenObjectKeyBelongsToAnotherExam()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string otherExamKey = $"exams/{Guid.NewGuid()}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, StorageHolding(otherExamKey));

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, otherExamKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.ObjectKeyNotOwnedByExam);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ownerId: Guid.NewGuid());
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, objectKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(examId));
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenTheSameNameAndVersionIsAddedTwice()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string firstKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";
        string secondKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        IStorageService storageService = Substitute.For<IStorageService>();
        storageService.StatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new StorageObjectInfo(callInfo.Arg<string>(), 3, "application/zip"));

        AddExamDependency.Handler handler = CreateHandler(context, storageService);

        await handler.Handle(
            new AddExamDependency.Command(examId, firstKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, secondKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.DependencyAlreadyAdded);
        (await context.ExamDependencies.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenTheSameObjectIsCommittedTwice()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, StorageHolding(objectKey));
        var command = new AddExamDependency.Command(examId, objectKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64);

        await handler.Handle(command, CancellationToken.None);

        // Act
        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.ContentAlreadyCommitted);
        (await context.ExamDependencies.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenExamIsNoLongerDraft()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, objectKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotDraft(ExamPackageStatus.Published));
        (await context.ExamDependencies.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_TakeSizeAndContentTypeFromStorageRatherThanTheRequest()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(
            context,
            StorageHolding(objectKey, sizeBytes: 90_112, contentType: "application/gzip"));

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, objectKey, " GCC ", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        ExamDependency dependency = await context.ExamDependencies.SingleAsync();
        dependency.SizeBytes.ShouldBe(90_112);
        dependency.ContentType!.Value.ShouldBe("application/gzip");
        dependency.ObjectKey!.Value.ShouldBe(objectKey);
        dependency.Name!.Value.ShouldBe("GCC");
        dependency.Version!.Value.ShouldBe("13.2.0");
        dependency.Platform.ShouldBe(DependencyPlatform.LinuxX64);
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenDependencyIsRecorded()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        await handler.Handle(
            new AddExamDependency.Command(examId, objectKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is ExamDependencyAddedDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    // The ceiling cannot be applied while the client uploads - a presigned URL accepts whatever it
    // is given - so it is enforced here, and the oversized object is removed rather than orphaned.
    [Fact]
    public async Task Handle_Should_RejectAndDeleteAnOversizedDependency()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        IStorageService storageService = StorageHolding(objectKey, sizeBytes: 5_000);
        AddExamDependency.Handler handler = CreateHandler(context, storageService, maxDependencyBytes: 1_000);

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, objectKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Exams.DependencyTooLarge");
        (await context.ExamDependencies.CountAsync()).ShouldBe(0);

        await storageService.Received(1).DeleteAsync(objectKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_AcceptADependencyAtExactlyTheLimit()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        IStorageService storageService = StorageHolding(objectKey, sizeBytes: 1_000);
        AddExamDependency.Handler handler = CreateHandler(context, storageService, maxDependencyBytes: 1_000);

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, objectKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        await storageService.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    // The point of the platform: one tool, one archive per operating system.
    [Fact]
    public async Task Handle_Should_AcceptTheSameNameAndVersionForAnotherPlatform()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string linuxKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";
        string windowsKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, StorageHoldingAnything());

        await handler.Handle(
            new AddExamDependency.Command(examId, linuxKey, "GCC", "13.2.0", DependencyPlatform.LinuxX64),
            CancellationToken.None);

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, windowsKey, "GCC", "13.2.0", DependencyPlatform.WindowsX64),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await context.ExamDependencies.Select(d => d.Platform).ToListAsync())
            .ShouldBe([DependencyPlatform.LinuxX64, DependencyPlatform.WindowsX64], ignoreOrder: true);
    }

    // An Any build next to a platform build of the same tool would hand a client two copies.
    [Theory]
    [InlineData(DependencyPlatform.Any, DependencyPlatform.WindowsX64)]
    [InlineData(DependencyPlatform.WindowsX64, DependencyPlatform.Any)]
    public async Task Handle_Should_ReturnConflict_WhenAnAnyBuildMeetsAPlatformBuildOfTheSameTool(
        DependencyPlatform first,
        DependencyPlatform second)
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string firstKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";
        string secondKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, StorageHoldingAnything());

        await handler.Handle(
            new AddExamDependency.Command(examId, firstKey, "GCC", "13.2.0", first),
            CancellationToken.None);

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, secondKey, "GCC", "13.2.0", second),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.DependencyAlreadyAdded);
        (await context.ExamDependencies.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_WriteNoRow_WhenThePlatformIsMissing()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);
        string objectKey = $"exams/{examId}/dependencies/{Guid.NewGuid()}";

        AddExamDependency.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        Result<Guid> result = await handler.Handle(
            new AddExamDependency.Command(examId, objectKey, "GCC", "13.2.0", null),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.InvalidDependencyPlatform);
        (await context.ExamDependencies.CountAsync()).ShouldBe(0);
    }

    // Required, not defaulted to Any: a Windows-only compiler must never be offered to Linux
    // students because the professor forgot to say.
    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public void Validator_Should_RejectAMissingOrUnknownPlatform(int? platform)
    {
        // Arrange
        var validator = new AddExamDependency.Validator();
        var command = new AddExamDependency.Command(
            Guid.NewGuid(),
            $"exams/{Guid.NewGuid()}/dependencies/{Guid.NewGuid()}",
            "GCC",
            "13.2.0",
            (DependencyPlatform?)platform);

        // Act
        ValidationResult result = validator.Validate(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(AddExamDependency.Command.Platform));
    }

    private static IStorageService StorageHoldingAnything()
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService.StatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new StorageObjectInfo(callInfo.Arg<string>(), 3, "application/zip"));

        return storageService;
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
