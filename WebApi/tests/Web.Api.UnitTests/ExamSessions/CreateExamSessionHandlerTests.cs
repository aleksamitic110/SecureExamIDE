using System.Text;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Crypto;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.ExamSessions;

public sealed class CreateExamSessionHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc);
    private const string Sha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private sealed class RecordingStorage : IStorageService
    {
        public Dictionary<string, byte[]> Objects { get; } = [];

        public Dictionary<string, byte[]> Existing { get; } = [];

        public async Task PutAsync(
            string objectKey,
            Stream content,
            string contentType,
            string? sha256,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Objects[objectKey] = buffer.ToArray();
        }

        public Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Existing[objectKey]));

        public Task<StorageObjectInfo?> StatAsync(string objectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<StorageObjectInfo?>(null);

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> CreatePresignedUploadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default) =>
            Task.FromResult("https://storage.test/upload");

        public Task<string> CreatePresignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default) =>
            Task.FromResult("https://storage.test/download");
    }

    private static CreateExamSession.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new CreateExamSession.Handler(
            context,
            userContext,
            storageService,
            new CryptoService(),
            new OneTimeCodeProvider(),
            dateTimeProvider);
    }

    [Fact]
    public async Task Handle_Should_SealThePackageAndStoreOnlyTheCodesDigest()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var storage = new RecordingStorage();
        Guid examId = await SeedPublishedExamWithFileAsync(context, storage, "Task 1: build a parser.");

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        Result<CreateExamSession.Response> result = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(7), Now.AddDays(7).AddHours(3)),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        ExamSession session = await context.ExamSessions.SingleAsync();
        string code = result.Value.OneTimeCode;

        // The code is returned once and must be unrecoverable from what was persisted.
        session.OneTimeCodeHash!.Value.ShouldBe(new OneTimeCodeProvider().Hash(code));
        session.OneTimeCodeHash.Value.ShouldNotContain(code.Replace("-", "", StringComparison.Ordinal));

        storage.Objects.Keys.ShouldContain(session.PackageObjectKey!.Value);
        storage.Objects.Keys.ShouldContain(session.HeaderObjectKey!.Value);
    }

    // The exam tasks must not be readable in what is written to storage - that is the whole point
    // of sealing at all.
    [Fact]
    public async Task Handle_Should_WriteCiphertextThatDoesNotContainTheTasks()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var storage = new RecordingStorage();
        Guid examId = await SeedPublishedExamWithFileAsync(context, storage, "Task 1: build a parser.");

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        Result<CreateExamSession.Response> result = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(7), Now.AddDays(7).AddHours(3)),
            CancellationToken.None);

        // Assert
        ExamSession session = await context.ExamSessions.SingleAsync();
        byte[] package = storage.Objects[session.PackageObjectKey!.Value];

        Encoding.UTF8.GetString(package).ShouldNotContain("build a parser");
        result.Value.PackageSizeBytes.ShouldBe(package.LongLength);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenTheExamIsStillADraft()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var storage = new RecordingStorage();
        Guid examId = await SeedPublishedExamWithFileAsync(context, storage, "Task", ExamPackageStatus.Draft);

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        Result<CreateExamSession.Response> result = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(7), Now.AddDays(7).AddHours(3)),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.ExamNotPublished);
        storage.Objects.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var storage = new RecordingStorage();
        Guid examId = await SeedPublishedExamWithFileAsync(
            context, storage, "Task", ExamPackageStatus.Published, ownerId: Guid.NewGuid());

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        Result<CreateExamSession.Response> result = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(7), Now.AddDays(7).AddHours(3)),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(examId));
    }

    [Fact]
    public async Task Handle_Should_RejectAPeriodThatEndsBeforeItStarts()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var storage = new RecordingStorage();
        Guid examId = await SeedPublishedExamWithFileAsync(context, storage, "Task");

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        Result<CreateExamSession.Response> result = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(7), Now.AddDays(6)),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.EndsBeforeItStarts);
    }

    [Fact]
    public async Task Handle_Should_RejectASittingThatIsAlreadyOver()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var storage = new RecordingStorage();
        Guid examId = await SeedPublishedExamWithFileAsync(context, storage, "Task");

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        Result<CreateExamSession.Response> result = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(-3), Now.AddDays(-2)),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.AlreadyOver);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenTheExamHasNoFiles()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var storage = new RecordingStorage();
        Guid examId = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        Result<CreateExamSession.Response> result = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(7), Now.AddDays(7).AddHours(3)),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.NoFiles);
    }

    // Two sittings of the same exam must not share a code, or the September retake would open with
    // the code that circulated in January.
    [Fact]
    public async Task Handle_Should_GiveEachSittingItsOwnCodeAndCiphertext()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var storage = new RecordingStorage();
        Guid examId = await SeedPublishedExamWithFileAsync(context, storage, "Task 1: build a parser.");

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        Result<CreateExamSession.Response> january = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(7), Now.AddDays(7).AddHours(3)),
            CancellationToken.None);

        Result<CreateExamSession.Response> september = await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(200), Now.AddDays(200).AddHours(3)),
            CancellationToken.None);

        // Assert
        january.Value.OneTimeCode.ShouldNotBe(september.Value.OneTimeCode);

        List<ExamSession> sessions = await context.ExamSessions.ToListAsync();
        sessions.Count.ShouldBe(2);
        sessions[0].PackageObjectKey!.Value.ShouldNotBe(sessions[1].PackageObjectKey!.Value);
        storage.Objects[sessions[0].PackageObjectKey!.Value]
            .ShouldNotBe(storage.Objects[sessions[1].PackageObjectKey!.Value]);
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenTheSessionIsCreated()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        var storage = new RecordingStorage();
        Guid examId = await SeedPublishedExamWithFileAsync(context, storage, "Task");

        CreateExamSession.Handler handler = CreateHandler(context, storage);

        // Act
        await handler.Handle(
            new CreateExamSession.Command(examId, Now.AddDays(7), Now.AddDays(7).AddHours(3)),
            CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is ExamSessionCreatedDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    private static async Task<Guid> SeedPublishedExamWithFileAsync(
        ApplicationDbContext context,
        RecordingStorage storage,
        string content,
        ExamPackageStatus status = ExamPackageStatus.Published,
        Guid? ownerId = null)
    {
        Guid examId = await SeedExamAsync(context, ownerId ?? ProfessorId, status);

        Web.Api.Common.ValueObjects.ObjectKey objectKey = ExamObjectKeys.NewFileKey(examId);
        storage.Existing[objectKey.Value] = Encoding.UTF8.GetBytes(content);

        context.ExamFiles.Add(new ExamFile
        {
            Id = Guid.NewGuid(),
            ExamPackageId = examId,
            FileName = "task.txt".AsFileName(),
            ContentType = "text/plain".AsContentType(),
            ObjectKey = objectKey,
            SizeBytes = content.Length,
            Sha256 = Sha256.AsSha256(),
            CreatedAt = Now
        });

        await context.SaveChangesAsync();

        return examId;
    }

    private static async Task<Guid> SeedExamAsync(
        ApplicationDbContext context,
        Guid ownerId,
        ExamPackageStatus status)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Compilers".AsExamTitle(),
            Description = "Final exam".AsExamDescription(),
            Subject = "Compiler Construction".AsExamSubject(),
            OwnerProfessorId = ownerId,
            Status = status,
            CreatedAt = Now,
            PublishedAt = status == ExamPackageStatus.Published ? Now : null
        };

        context.ExamPackages.Add(exam);
        await context.SaveChangesAsync();

        return exam.Id;
    }
}
