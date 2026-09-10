using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Exams;
using Web.Api.Features.Submissions;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Submissions;

public sealed class SubmitSolutionHandlerTests : BaseHandlerTest
{
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);
    private const string Sha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static SubmitSolution.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService,
        Guid? deviceId = null)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(StudentId);
        userContext.DeviceId.Returns(deviceId ?? DeviceId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new SubmitSolution.Handler(context, userContext, storageService, dateTimeProvider);
    }

    private static IStorageService StorageHolding(string objectKey, long sizeBytes = 42)
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService.StatAsync(objectKey, Arg.Any<CancellationToken>())
            .Returns(new StorageObjectInfo(objectKey, sizeBytes, "application/octet-stream"));

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
    public async Task Handle_Should_RecordTheSubmissionAndTheMachineItCameFrom()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string objectKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;

        SubmitSolution.Handler handler = CreateHandler(context, StorageHolding(objectKey, sizeBytes: 1_234));

        // Act
        Result<SubmitSolution.Response> result = await handler.Handle(
            new SubmitSolution.Command(sessionId, objectKey, Sha256),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        Submission submission = await context.Submissions.SingleAsync();
        submission.StudentId.ShouldBe(StudentId);
        submission.DeviceCredentialId.ShouldBe(DeviceId);
        submission.SizeBytes.ShouldBe(1_234);
        submission.Sha256!.Value.ShouldBe(Sha256);
        submission.SubmittedAt.ShouldBe(Now);
    }

    // Submitting must come from the machine that sat the exam, so an ordinary password login is
    // refused even though its token is perfectly valid.
    [Fact]
    public async Task Handle_Should_RefuseATokenWithoutADeviceClaim()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string objectKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;

        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(StudentId);
        userContext.DeviceId.Returns((Guid?)null);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        var handler = new SubmitSolution.Handler(
            context, userContext, StorageHolding(objectKey), dateTimeProvider);

        // Act
        Result<SubmitSolution.Response> result = await handler.Handle(
            new SubmitSolution.Command(sessionId, objectKey, Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.DeviceTokenRequired);
        (await context.Submissions.CountAsync()).ShouldBe(0);
    }

    // The property the exam mode exists to provide: once handed in, the work cannot be replaced.
    [Fact]
    public async Task Handle_Should_RefuseASecondSubmissionForTheSameSitting()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);

        string first = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;
        string second = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;

        IStorageService storageService = Substitute.For<IStorageService>();
        storageService.StatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new StorageObjectInfo(callInfo.Arg<string>(), 42, "application/octet-stream"));

        SubmitSolution.Handler handler = CreateHandler(context, storageService);

        await handler.Handle(new SubmitSolution.Command(sessionId, first, Sha256), CancellationToken.None);

        // Act
        Result<SubmitSolution.Response> result = await handler.Handle(
            new SubmitSolution.Command(sessionId, second, Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.AlreadySubmitted);
        (await context.Submissions.CountAsync()).ShouldBe(1);
    }

    // The student id is part of the key prefix precisely so this cannot work.
    [Fact]
    public async Task Handle_Should_RefuseAKeyUploadedByAnotherStudent()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string otherStudentsKey = SubmissionObjectKeys.NewSolutionKey(sessionId, Guid.NewGuid()).Value;

        IStorageService storageService = StorageHolding(otherStudentsKey);
        SubmitSolution.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<SubmitSolution.Response> result = await handler.Handle(
            new SubmitSolution.Command(sessionId, otherStudentsKey, Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.ObjectKeyNotOwned);

        // The ownership guard runs before storage is consulted at all.
        await storageService.DidNotReceiveWithAnyArgs().StatAsync(default!, default);
    }

    [Fact]
    public async Task Handle_Should_WriteNoRow_WhenTheSolutionWasNeverUploaded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string objectKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;

        SubmitSolution.Handler handler = CreateHandler(context, EmptyStorage());

        // Act
        Result<SubmitSolution.Response> result = await handler.Handle(
            new SubmitSolution.Command(sessionId, objectKey, Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.SolutionNotUploaded);
        (await context.Submissions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_RefuseASittingThatHasNotStarted()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context, startsAt: Now.AddDays(1));
        string objectKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;

        SubmitSolution.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        Result<SubmitSolution.Response> result = await handler.Handle(
            new SubmitSolution.Command(sessionId, objectKey, Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.SessionNotStarted);
    }

    // A student works offline and uploads when a connection returns, which may be well after the
    // sitting ended. Arriving late is recorded, not refused.
    [Fact]
    public async Task Handle_Should_AcceptASolutionThatArrivesAfterTheSittingEnded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(
            context, startsAt: Now.AddDays(-3), endsAt: Now.AddDays(-3).AddHours(3));

        string objectKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;
        SubmitSolution.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        Result<SubmitSolution.Response> result = await handler.Handle(
            new SubmitSolution.Command(sessionId, objectKey, Sha256),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.SubmittedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Handle_Should_RefuseACancelledSitting()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context, isActive: false);
        string objectKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;

        SubmitSolution.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        Result<SubmitSolution.Response> result = await handler.Handle(
            new SubmitSolution.Command(sessionId, objectKey, Sha256),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.Cancelled);
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenTheSolutionIsRecorded()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        Guid sessionId = await SeedSessionAsync(context);
        string objectKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;

        SubmitSolution.Handler handler = CreateHandler(context, StorageHolding(objectKey));

        // Act
        await handler.Handle(
            new SubmitSolution.Command(sessionId, objectKey, Sha256),
            CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is SubmissionCreatedDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    private static async Task<Guid> SeedSessionAsync(
        ApplicationDbContext context,
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        bool isActive = true,
        ExamPackageStatus examStatus = ExamPackageStatus.Published)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Compilers".AsExamTitle(),
            Description = "Final".AsExamDescription(),
            Subject = "Compiler Construction".AsExamSubject(),
            OwnerProfessorId = Guid.NewGuid(),
            Status = examStatus,
            CreatedAt = Now.AddDays(-10),
            PublishedAt = Now.AddDays(-9)
        };

        var session = new ExamSession
        {
            Id = Guid.NewGuid(),
            ExamPackageId = exam.Id,
            CreatedByProfessorId = exam.OwnerProfessorId,
            StartsAt = startsAt ?? Now.AddHours(-2),
            EndsAt = endsAt ?? Now.AddHours(1),
            IsActive = isActive,
            OneTimeCodeHash = Sha256.AsSha256(),
            PackageObjectKey = ObjectKey.Create($"exams/{exam.Id}/sessions/x/package.bin").Value,
            HeaderObjectKey = ObjectKey.Create($"exams/{exam.Id}/sessions/x/package.hdr").Value,
            PackageSizeBytes = 100,
            PackageSha256 = Sha256.AsSha256(),
            CreatedAt = Now.AddDays(-5)
        };

        context.ExamPackages.Add(exam);
        context.ExamSessions.Add(session);
        await context.SaveChangesAsync();

        return session.Id;
    }
}
