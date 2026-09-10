using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Submissions;

// What a student has handed in, and the only way for them to confirm an upload actually arrived
// after working offline. Scoped to the caller, so it can never show anyone else's work.
public static class GetMySubmissions
{
    public sealed record Query(int Page, int PageSize) : IQuery<PagedList<Response>>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public Guid SessionId { get; init; }

        public Guid ExamId { get; init; }

        public string ExamTitle { get; init; }

        public DateTime SessionStartsAt { get; init; }

        public DateTime SubmittedAt { get; init; }

        public long SizeBytes { get; init; }

        // Returned so a student can check the server received exactly what they sent.
        public string Sha256 { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context, IUserContext userContext)
        : IQueryHandler<Query, PagedList<Response>>
    {
        public async Task<Result<PagedList<Response>>> Handle(Query query, CancellationToken cancellationToken)
        {
            IQueryable<Response> submissions =
                from submission in context.Submissions.AsNoTracking()
                join session in context.ExamSessions on submission.ExamSessionId equals session.Id
                join exam in context.ExamPackages on session.ExamPackageId equals exam.Id
                where submission.StudentId == userContext.UserId
                orderby submission.SubmittedAt descending, submission.Id
                select new Response
                {
                    Id = submission.Id,
                    SessionId = session.Id,
                    ExamId = exam.Id,
                    ExamTitle = exam.Title.Value,
                    SessionStartsAt = session.StartsAt,
                    SubmittedAt = submission.SubmittedAt,
                    SizeBytes = submission.SizeBytes,
                    Sha256 = submission.Sha256.Value
                };

            return await PagedList<Response>.CreateAsync(
                submissions,
                query.Page,
                query.PageSize,
                cancellationToken);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("submissions/mine", async (
                int? page,
                int? pageSize,
                IQueryHandler<Query, PagedList<Response>> handler,
                CancellationToken cancellationToken) =>
            {
                var query = new Query(page ?? 1, pageSize ?? PagedList<Response>.DefaultPageSize);

                Result<PagedList<Response>> result = await handler.Handle(query, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.SubmissionsSubmit)
            .WithTags(Tags.Submissions);
        }
    }
}
