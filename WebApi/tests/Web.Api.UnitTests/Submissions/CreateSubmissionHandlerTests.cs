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

public sealed class CreateSubmissionHandlerTests : BaseHandlerTest
{
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);
    private const string SolutionSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private static readonly string ActivityLogSha256 = new('a', 64);

    private static CreateSubmission.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(StudentId);
        userContext.DeviceId.Returns(DeviceId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new CreateSubmission.Handler(context, userContext, storageService, dateTimeProvider);
    }

    private static CreateSubmission.Command CommandFor(Guid sessionId, string solutionKey, string activityLogKey) =>
        new(sessionId, solutionKey, activityLogKey);

    private static CreateSubmission.Command FreshCommandFor(Guid sessionId) =>
        CommandFor(
            sessionId,
            SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value,
            SubmissionObjectKeys.NewActivityLogKey(sessionId, StudentId).Value);

    // Storage that holds exactly the given objects, each with the digest phase one recorded for it.
    private static IStorageService StorageHolding(params (string ObjectKey, long SizeBytes, string? Sha256)[] objects)
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService.StatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((StorageObjectInfo?)null);

        foreach ((string objectKey, long sizeBytes, string? sha256) in objects)
        {
            storageService.StatAsync(objectKey, Arg.Any<CancellationToken>())
                .Returns(new StorageObjectInfo(objectKey, sizeBytes, "application/octet-stream", sha256));
        }

        return storageService;
    }

    // Storage that claims to hold every key it is asked about.
    private static IStorageService StorageHoldingEverything()
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService.StatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new StorageObjectInfo(
                callInfo.Arg<string>(), 42, "application/octet-stream", SolutionSha256));

        return storageService;
    }

    // Sizes and digests both come from storage: the command carries nothing but the two keys.
    [Fact]
    public async Task Handle_Should_RecordBothPartsFromStorageAndTheMachineTheyCameFrom()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string solutionKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;
        string activityLogKey = SubmissionObjectKeys.NewActivityLogKey(sessionId, StudentId).Value;

        CreateSubmission.Handler handler = CreateHandler(
            context,
            StorageHolding((solutionKey, 1_234, SolutionSha256), (activityLogKey, 567, ActivityLogSha256)));

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            CommandFor(sessionId, solutionKey, activityLogKey),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        Submission submission = await context.Submissions.SingleAsync();
        submission.StudentId.ShouldBe(StudentId);
        submission.DeviceCredentialId.ShouldBe(DeviceId);
        submission.SolutionObjectKey.Value.ShouldBe(solutionKey);
        submission.SolutionSizeBytes.ShouldBe(1_234);
        submission.SolutionSha256.Value.ShouldBe(SolutionSha256);
        submission.ActivityLogObjectKey.Value.ShouldBe(activityLogKey);
        submission.ActivityLogSizeBytes.ShouldBe(567);
        submission.ActivityLogSha256.Value.ShouldBe(ActivityLogSha256);
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

        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(StudentId);
        userContext.DeviceId.Returns((Guid?)null);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        var handler = new CreateSubmission.Handler(
            context, userContext, StorageHoldingEverything(), dateTimeProvider);

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            FreshCommandFor(sessionId),
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
        CreateSubmission.Handler handler = CreateHandler(context, StorageHoldingEverything());

        await handler.Handle(FreshCommandFor(sessionId), CancellationToken.None);

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            FreshCommandFor(sessionId),
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
        string otherStudentsSolution = SubmissionObjectKeys.NewSolutionKey(sessionId, Guid.NewGuid()).Value;
        string activityLogKey = SubmissionObjectKeys.NewActivityLogKey(sessionId, StudentId).Value;

        IStorageService storageService = StorageHoldingEverything();
        CreateSubmission.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            CommandFor(sessionId, otherStudentsSolution, activityLogKey),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.ObjectKeyNotOwned);

        // The ownership guard runs before storage is consulted at all.
        await storageService.DidNotReceiveWithAnyArgs().StatAsync(default!, default);
    }

    // The part is in the key prefix too, so a second copy of the solution cannot be handed in as
    // the activity log - the log has to be a log.
    [Fact]
    public async Task Handle_Should_RefuseASolutionKeyInPlaceOfTheActivityLog()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string solutionKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;
        string anotherSolutionKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;

        CreateSubmission.Handler handler = CreateHandler(context, StorageHoldingEverything());

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            CommandFor(sessionId, solutionKey, anotherSolutionKey),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.ObjectKeyNotOwned);
        (await context.Submissions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_WriteNoRow_WhenTheSolutionWasNeverUploaded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string solutionKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;
        string activityLogKey = SubmissionObjectKeys.NewActivityLogKey(sessionId, StudentId).Value;

        CreateSubmission.Handler handler = CreateHandler(
            context, StorageHolding((activityLogKey, 10, ActivityLogSha256)));

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            CommandFor(sessionId, solutionKey, activityLogKey),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.SolutionNotUploaded);
        (await context.Submissions.CountAsync()).ShouldBe(0);
    }

    // A submission is the pair. A solution whose log never reached storage is not half-recorded.
    [Fact]
    public async Task Handle_Should_WriteNoRow_WhenTheActivityLogWasNeverUploaded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string solutionKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;
        string activityLogKey = SubmissionObjectKeys.NewActivityLogKey(sessionId, StudentId).Value;

        CreateSubmission.Handler handler = CreateHandler(
            context, StorageHolding((solutionKey, 10, SolutionSha256)));

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            CommandFor(sessionId, solutionKey, activityLogKey),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.ActivityLogNotUploaded);
        (await context.Submissions.CountAsync()).ShouldBe(0);
    }

    // Phase one always records a digest, so an object without one did not come through it and the
    // server has no measurement of its own to record.
    [Fact]
    public async Task Handle_Should_WriteNoRow_WhenAStoredPartCarriesNoDigest()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        string solutionKey = SubmissionObjectKeys.NewSolutionKey(sessionId, StudentId).Value;
        string activityLogKey = SubmissionObjectKeys.NewActivityLogKey(sessionId, StudentId).Value;

        CreateSubmission.Handler handler = CreateHandler(
            context,
            StorageHolding((solutionKey, 10, SolutionSha256), (activityLogKey, 10, null)));

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            CommandFor(sessionId, solutionKey, activityLogKey),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.ActivityLogNotUploaded);
        (await context.Submissions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_RefuseASittingThatHasNotStarted()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context, startsAt: Now.AddDays(1));

        CreateSubmission.Handler handler = CreateHandler(context, StorageHoldingEverything());

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            FreshCommandFor(sessionId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.SessionNotStarted);
    }

    // A student works offline and uploads when a connection returns, which may be well after the
    // sitting ended. Arriving late is recorded, not refused.
    [Fact]
    public async Task Handle_Should_AcceptASubmissionThatArrivesAfterTheSittingEnded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(
            context, startsAt: Now.AddDays(-3), endsAt: Now.AddDays(-3).AddHours(3));

        CreateSubmission.Handler handler = CreateHandler(context, StorageHoldingEverything());

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            FreshCommandFor(sessionId),
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

        CreateSubmission.Handler handler = CreateHandler(context, StorageHoldingEverything());

        // Act
        Result<CreateSubmission.Response> result = await handler.Handle(
            FreshCommandFor(sessionId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.Cancelled);
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenTheSubmissionIsRecorded()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        Guid sessionId = await SeedSessionAsync(context);

        CreateSubmission.Handler handler = CreateHandler(context, StorageHoldingEverything());

        // Act
        await handler.Handle(FreshCommandFor(sessionId), CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is SubmissionCreatedDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    private static async Task<Guid> SeedSessionAsync(
        ApplicationDbContext context,
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        bool isActive = true)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Compilers".AsExamTitle(),
            Description = "Final".AsExamDescription(),
            Subject = "Compiler Construction".AsExamSubject(),
            OwnerProfessorId = Guid.NewGuid(),
            Status = ExamPackageStatus.Published,
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
            OneTimeCodeHash = SolutionSha256.AsSha256(),
            PackageObjectKey = ObjectKey.Create($"exams/{exam.Id}/sessions/x/package.bin").Value,
            HeaderObjectKey = ObjectKey.Create($"exams/{exam.Id}/sessions/x/package.hdr").Value,
            PackageSizeBytes = 100,
            PackageSha256 = SolutionSha256.AsSha256(),
            CreatedAt = Now.AddDays(-5)
        };

        context.ExamPackages.Add(exam);
        context.ExamSessions.Add(session);
        await context.SaveChangesAsync();

        return session.Id;
    }
}
