using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.ExamSessions;
using Web.Api.UnitTests.Abstractions;
using Web.Api.UnitTests.Submissions;

namespace Web.Api.UnitTests.ExamSessions;

public sealed class CancelExamSessionHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private static readonly DateTime StartsAt = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndsAt = StartsAt.AddHours(3);

    private static CancelExamSession.Handler CreateHandler(ApplicationDbContext context)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new CancelExamSession.Handler(context, userContext);
    }

    private static async Task<bool> IsActiveAsync(ApplicationDbContext context, Guid sessionId) =>
        await context.ExamSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.IsActive)
            .SingleAsync();

    [Fact]
    public async Task Handle_Should_CancelTheSitting()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, ProfessorId, StartsAt, EndsAt);

        CancelExamSession.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new CancelExamSession.Command(sessionId), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await IsActiveAsync(context, sessionId)).ShouldBeFalse();
    }

    // Cancelling after the end is deliberate: it is also how a professor stops late uploads.
    [Fact]
    public async Task Handle_Should_CancelASittingThatHasAlreadyEnded()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(
            context, ProfessorId, StartsAt.AddYears(-1), EndsAt.AddYears(-1));

        CancelExamSession.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new CancelExamSession.Command(sessionId), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await IsActiveAsync(context, sessionId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenTheSittingIsAlreadyCancelled()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, ProfessorId, StartsAt, EndsAt);

        CancelExamSession.Handler handler = CreateHandler(context);
        await handler.Handle(new CancelExamSession.Command(sessionId), CancellationToken.None);

        // Act
        Result result = await handler.Handle(new CancelExamSession.Command(sessionId), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.Cancelled);
    }

    // Reported as missing rather than forbidden, and the sitting is left exactly as it was.
    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheSittingBelongsToAnotherProfessorsExam()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, Guid.NewGuid(), StartsAt, EndsAt);

        CancelExamSession.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new CancelExamSession.Command(sessionId), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamSessionErrors.NotFound(sessionId));
        (await IsActiveAsync(context, sessionId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenTheSittingIsCancelled()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, ProfessorId, StartsAt, EndsAt);

        CancelExamSession.Handler handler = CreateHandler(context);

        // Act
        await handler.Handle(new CancelExamSession.Command(sessionId), CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is ExamSessionCancelledDomainEvent)),
            Arg.Any<CancellationToken>());
    }
}
