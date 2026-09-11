using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.ExamSessions;
using Web.Api.UnitTests.Abstractions;
using Web.Api.UnitTests.Submissions;

namespace Web.Api.UnitTests.ExamSessions;

public sealed class GetMySessionsHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();
    private static readonly DateTime January = new(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime September = new(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc);

    private static GetMySessions.Handler CreateHandler(ApplicationDbContext context)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new GetMySessions.Handler(context, userContext);
    }

    private static GetMySessions.Query QueryFor(Guid? examId = null) => new(1, 20, examId);

    private static async Task CancelAsync(ApplicationDbContext context, Guid sessionId)
    {
        ExamSession session = await context.ExamSessions.SingleAsync(s => s.Id == sessionId);
        session.IsActive = false;
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> ExamOfAsync(ApplicationDbContext context, Guid sessionId) =>
        await context.ExamSessions
            .Where(s => s.Id == sessionId)
            .Select(s => s.ExamPackageId)
            .SingleAsync();

    // The whole point of the list: a cancelled sitting no longer disappears from the professor's view.
    [Fact]
    public async Task Handle_Should_ListTheCallersSittings_CancelledIncluded_NewestFirst()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid january = await ReviewSeed.SeedSessionAsync(context, ProfessorId, January, January.AddHours(3));
        Guid september = await ReviewSeed.SeedSessionAsync(context, ProfessorId, September, September.AddHours(3));
        await ReviewSeed.SeedSessionAsync(context, Guid.NewGuid(), September, September.AddHours(3));
        await CancelAsync(context, january);

        GetMySessions.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetMySessions.Response>> result = await handler.Handle(QueryFor(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(s => s.Id).ShouldBe([september, january]);
        result.Value.Items[0].IsCancelled.ShouldBeFalse();
        result.Value.Items[1].IsCancelled.ShouldBeTrue();
        result.Value.Items[0].ExamTitle.ShouldBe("Compilers");
    }

    [Fact]
    public async Task Handle_Should_ReturnOnlyTheRequestedExamsSittings()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid wanted = await ReviewSeed.SeedSessionAsync(context, ProfessorId, January, January.AddHours(3));
        await ReviewSeed.SeedSessionAsync(context, ProfessorId, September, September.AddHours(3));

        GetMySessions.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetMySessions.Response>> result = await handler.Handle(
            QueryFor(await ExamOfAsync(context, wanted)),
            CancellationToken.None);

        // Assert
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().Id.ShouldBe(wanted);
    }

    // Filtering by someone else's exam matches nothing and so reveals nothing about it.
    [Fact]
    public async Task Handle_Should_ReturnNothing_WhenFilteringByAnotherProfessorsExam()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid theirs = await ReviewSeed.SeedSessionAsync(context, Guid.NewGuid(), January, January.AddHours(3));

        GetMySessions.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetMySessions.Response>> result = await handler.Handle(
            QueryFor(await ExamOfAsync(context, theirs)),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_CountTheSubmissionsOfEachSitting()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid sessionId = await ReviewSeed.SeedSessionAsync(context, ProfessorId, January, January.AddHours(3));
        await ReviewSeed.SeedSubmissionAsync(context, sessionId, January.AddHours(1));
        await ReviewSeed.SeedSubmissionAsync(context, sessionId, January.AddHours(2));

        GetMySessions.Handler handler = CreateHandler(context);

        // Act
        Result<PagedList<GetMySessions.Response>> result = await handler.Handle(QueryFor(), CancellationToken.None);

        // Assert
        result.Value.Items.Single().SubmissionCount.ShouldBe(2);
    }
}
