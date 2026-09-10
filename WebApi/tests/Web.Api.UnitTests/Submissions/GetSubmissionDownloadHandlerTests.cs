using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Submissions;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Submissions;

public sealed class GetSubmissionDownloadHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private static readonly DateTime StartsAt = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = StartsAt.AddDays(1);

    private static GetSubmissionDownload.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new GetSubmissionDownload.Handler(context, userContext, storageService, dateTimeProvider);
    }

    // A storage double that signs a recognisable URL for whatever key it is given.
    private static IStorageService SigningStorage()
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService
            .CreatePresignedDownloadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => $"https://storage.test/{callInfo.Arg<string>()}");

        return storageService;
    }

    [Fact]
    public async Task Handle_Should_ReturnShortLivedUrlsForBothPartsWithTheirDigests()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, ProfessorId, StartsAt, StartsAt.AddHours(3));
        Submission submission = await ReviewSeed.SeedSubmissionAsync(context, sessionId, StartsAt.AddHours(2));

        IStorageService storageService = SigningStorage();
        GetSubmissionDownload.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<GetSubmissionDownload.Response> result = await handler.Handle(
            new GetSubmissionDownload.Query(submission.Id),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        GetSubmissionDownload.Response download = result.Value;
        download.SolutionUrl.ShouldBe($"https://storage.test/{submission.SolutionObjectKey.Value}");
        download.ActivityLogUrl.ShouldBe($"https://storage.test/{submission.ActivityLogObjectKey.Value}");
        download.ExpiresAt.ShouldBe(Now.AddMinutes(15));
        download.SolutionSha256.ShouldBe(ReviewSeed.SolutionDigest);
        download.ActivityLogSha256.ShouldBe(ReviewSeed.ActivityLogDigest);

        await storageService.Received(2).CreatePresignedDownloadUrlAsync(
            Arg.Any<string>(), TimeSpan.FromMinutes(15), Arg.Any<CancellationToken>());
    }

    // Reported as missing rather than forbidden, and no URL is signed at all.
    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheSubmissionIsForAnotherProfessorsExam()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, Guid.NewGuid(), StartsAt, StartsAt.AddHours(3));
        Submission submission = await ReviewSeed.SeedSubmissionAsync(context, sessionId, StartsAt.AddHours(2));

        IStorageService storageService = SigningStorage();
        GetSubmissionDownload.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<GetSubmissionDownload.Response> result = await handler.Handle(
            new GetSubmissionDownload.Query(submission.Id),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.NotFound(submission.Id));

        await storageService.DidNotReceiveWithAnyArgs()
            .CreatePresignedDownloadUrlAsync(default!, default, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheSubmissionDoesNotExist()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var submissionId = Guid.NewGuid();

        GetSubmissionDownload.Handler handler = CreateHandler(context, SigningStorage());

        // Act
        Result<GetSubmissionDownload.Response> result = await handler.Handle(
            new GetSubmissionDownload.Query(submissionId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SubmissionErrors.NotFound(submissionId));
    }
}
