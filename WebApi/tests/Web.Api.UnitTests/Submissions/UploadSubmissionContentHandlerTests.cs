using System.Security.Cryptography;
using System.Text;
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

public sealed class UploadSubmissionContentHandlerTests : BaseHandlerTest
{
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);
    private const string ContentType = "application/octet-stream";

    private static UploadSubmissionContent.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(StudentId);
        userContext.DeviceId.Returns(DeviceId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new UploadSubmissionContent.Handler(context, userContext, storageService, dateTimeProvider);
    }

    [Fact]
    public async Task Handle_Should_StoreBothPartsUnderTheirOwnKeys()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        IStorageService storageService = Substitute.For<IStorageService>();
        UploadSubmissionContent.Handler handler = CreateHandler(context, storageService);

        using var solution = new MemoryStream(Encoding.UTF8.GetBytes("solution"));
        using var activityLog = new MemoryStream(Encoding.UTF8.GetBytes("activity log"));

        // Act
        Result<UploadSubmissionContent.Response> result = await handler.Handle(
            new UploadSubmissionContent.Command(sessionId, solution, ContentType, activityLog, ContentType),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        UploadSubmissionContent.UploadedPart storedSolution = result.Value.Solution;
        UploadSubmissionContent.UploadedPart storedActivityLog = result.Value.ActivityLog;

        SubmissionObjectKeys.IsSolutionOf(storedSolution.ObjectKey.AsObjectKey(), sessionId, StudentId)
            .ShouldBeTrue();
        SubmissionObjectKeys.IsActivityLogOf(storedActivityLog.ObjectKey.AsObjectKey(), sessionId, StudentId)
            .ShouldBeTrue();

        // Each digest is the server's own measurement of that part's bytes.
        storedSolution.Sha256.ShouldBe(Sha256Of("solution"));
        storedActivityLog.Sha256.ShouldBe(Sha256Of("activity log"));
        storedActivityLog.SizeBytes.ShouldBe("activity log".Length);

        // Each digest goes into storage with its bytes, which is what CreateSubmission reads back.
        await storageService.Received(1).PutAsync(
            storedSolution.ObjectKey, solution, ContentType, storedSolution.Sha256, Arg.Any<CancellationToken>());
        await storageService.Received(1).PutAsync(
            storedActivityLog.ObjectKey, activityLog, ContentType, storedActivityLog.Sha256, Arg.Any<CancellationToken>());
    }

    // Both parts are checked before either is written, so a request carrying half a submission
    // leaves nothing in storage.
    [Fact]
    public async Task Handle_Should_StoreNothing_WhenTheActivityLogIsEmpty()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await SeedSessionAsync(context);
        IStorageService storageService = Substitute.For<IStorageService>();
        UploadSubmissionContent.Handler handler = CreateHandler(context, storageService);

        using var solution = new MemoryStream(Encoding.UTF8.GetBytes("solution"));
        using var activityLog = new MemoryStream();

        // Act
        Result<UploadSubmissionContent.Response> result = await handler.Handle(
            new UploadSubmissionContent.Command(sessionId, solution, ContentType, activityLog, ContentType),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.EmptyActivityLog);
        await storageService.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default, default);
    }

    private static string Sha256Of(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static async Task<Guid> SeedSessionAsync(ApplicationDbContext context)
    {
        string digest = new('a', 64);

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
            StartsAt = Now.AddHours(-2),
            EndsAt = Now.AddHours(1),
            IsActive = true,
            OneTimeCodeHash = digest.AsSha256(),
            PackageObjectKey = ObjectKey.Create($"exams/{exam.Id}/sessions/x/package.bin").Value,
            HeaderObjectKey = ObjectKey.Create($"exams/{exam.Id}/sessions/x/package.hdr").Value,
            PackageSizeBytes = 100,
            PackageSha256 = digest.AsSha256(),
            CreatedAt = Now.AddDays(-5)
        };

        context.ExamPackages.Add(exam);
        context.ExamSessions.Add(session);
        await context.SaveChangesAsync();

        return session.Id;
    }
}
