using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.ExamSessions;

// Every sitting of the professor's own exams, cancelled ones included. The student view
// (GetExamSessions) shows only the active sittings of one published exam, so until this existed a
// professor had no list of what they had scheduled, and a cancelled sitting vanished from every
// list.
//
// The one-time code is not here and cannot be: it is shown once, when the sitting is created, and
// only its digest is stored.
public static class GetMySessions
{
    public sealed record Query(int Page, int PageSize, Guid? ExamId) : IQuery<PagedList<Response>>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public Guid ExamId { get; init; }

        public string ExamTitle { get; init; }

        public DateTime StartsAt { get; init; }

        public DateTime EndsAt { get; init; }

        public bool IsCancelled { get; init; }

        // How many students have handed in, so a professor sees at a glance whether the work has
        // arrived without opening the review list.
        public int SubmissionCount { get; init; }

        public DateTime CreatedAt { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context, IUserContext userContext)
        : IQueryHandler<Query, PagedList<Response>>
    {
        public async Task<Result<PagedList<Response>>> Handle(Query query, CancellationToken cancellationToken)
        {
            // Filtering by an exam the caller does not own simply matches nothing, which reveals
            // nothing about that exam.
            IQueryable<Response> sessions =
                from session in context.ExamSessions.AsNoTracking()
                join exam in context.ExamPackages on session.ExamPackageId equals exam.Id
                where exam.OwnerProfessorId == userContext.UserId
                    && (query.ExamId == null || session.ExamPackageId == query.ExamId)
                orderby session.StartsAt descending, session.Id
                select new Response
                {
                    Id = session.Id,
                    ExamId = exam.Id,
                    ExamTitle = exam.Title.Value,
                    StartsAt = session.StartsAt,
                    EndsAt = session.EndsAt,
                    IsCancelled = !session.IsActive,
                    SubmissionCount = context.Submissions.Count(s => s.ExamSessionId == session.Id),
                    CreatedAt = session.CreatedAt
                };

            return await PagedList<Response>.CreateAsync(
                sessions,
                query.Page,
                query.PageSize,
                cancellationToken);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("sessions/mine", async (
                int? page,
                int? pageSize,
                Guid? examId,
                IQueryHandler<Query, PagedList<Response>> handler,
                CancellationToken cancellationToken) =>
            {
                var query = new Query(
                    page ?? 1,
                    pageSize ?? PagedList<Response>.DefaultPageSize,
                    examId);

                Result<PagedList<Response>> result = await handler.Handle(query, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.ExamSessions);
        }
    }
}
