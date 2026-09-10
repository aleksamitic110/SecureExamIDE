using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Submissions;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Submissions;

public sealed class GetSessionSubmissionsHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private static readonly DateTime StartsAt = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndsAt = StartsAt.AddHours(3);

    private static GetSessionSubmissions.Handler CreateHandler(ApplicationDbContext context)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new GetSessionSubmissions.Handler(context, userContext);
    }

    private static GetSessionSubmissions.Query QueryFor(Guid sessionId) => new(sessionId, 1, 20);

    [Fact]
    public async Task Handle_Should_ListEachSubmissionWithItsStudentAndMachine()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, ProfessorId, StartsAt, EndsAt);
        Submission submission = await ReviewSeed.SeedSubmissionAsync(
            context, sessionId, StartsAt.AddHours(2), "Marko", "Markovic", "19252", "marko-thinkpad");

        GetSessionSubmissions.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetSessionSubmissions.Response>> result = await handler.Handle(
            QueryFor(sessionId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);

        GetSessionSubmissions.Response item = result.Value.Items.Single();
        item.Id.ShouldBe(submission.Id);
        item.StudentId.ShouldBe(submission.StudentId);
        item.StudentFirstName.ShouldBe("Marko");
        item.StudentLastName.ShouldBe("Markovic");
        item.StudentIndexNumber.ShouldBe("19252");
        item.DeviceName.ShouldBe("marko-thinkpad");
        item.SubmittedAfterSessionEnded.ShouldBeFalse();
        item.SolutionSha256.ShouldBe(ReviewSeed.SolutionDigest);
        item.ActivityLogSha256.ShouldBe(ReviewSeed.ActivityLogDigest);
        item.ActivityLogSizeBytes.ShouldBe(69);
    }

    // The end of a sitting is not enforced at submission, so it is reported here instead: the
    // professor decides what a late hand-in means.
    [Fact]
    public async Task Handle_Should_FlagWorkThatArrivedAfterTheSittingEnded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, ProfessorId, StartsAt, EndsAt);

        Submission onTime = await ReviewSeed.SeedSubmissionAsync(context, sessionId, EndsAt.AddMinutes(-1));
        Submission late = await ReviewSeed.SeedSubmissionAsync(context, sessionId, EndsAt.AddHours(5));

        GetSessionSubmissions.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetSessionSubmissions.Response>> result = await handler.Handle(
            QueryFor(sessionId),
            CancellationToken.None);

        // Assert - ordered by arrival.
        result.Value.Items.Select(i => i.Id).ShouldBe([onTime.Id, late.Id]);
        result.Value.Items[0].SubmittedAfterSessionEnded.ShouldBeFalse();
        result.Value.Items[1].SubmittedAfterSessionEnded.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_Should_ListOnlyTheRequestedSitting()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid january = await ReviewSeed.SeedSessionAsync(context, ProfessorId, StartsAt, EndsAt);
        Guid september = await ReviewSeed.SeedSessionAsync(
            context, ProfessorId, StartsAt.AddMonths(8), EndsAt.AddMonths(8));

        Submission januaryWork = await ReviewSeed.SeedSubmissionAsync(context, january, StartsAt.AddHours(1));
        await ReviewSeed.SeedSubmissionAsync(context, september, StartsAt.AddMonths(8).AddHours(1));

        GetSessionSubmissions.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetSessionSubmissions.Response>> result = await handler.Handle(
            QueryFor(january),
            CancellationToken.None);

        // Assert
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().Id.ShouldBe(januaryWork.Id);
    }

    // Reported as missing rather than forbidden, so one professor cannot probe for another's
    // sittings - the rule every owned resource in this API follows.
    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheSittingBelongsToAnotherProfessorsExam()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, Guid.NewGuid(), StartsAt, EndsAt);
        await ReviewSeed.SeedSubmissionAsync(context, sessionId, StartsAt.AddHours(1));

        GetSessionSubmissions.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetSessionSubmissions.Response>> result = await handler.Handle(
            QueryFor(sessionId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.NotFound(sessionId));
    }
}
