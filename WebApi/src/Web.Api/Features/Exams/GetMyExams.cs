using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Exams;

// A professor's own exams, drafts included - the list a professor comes back to. The catalog
// (GetPublishedExams) shows published exams only and GetExam needs an id, so until this existed a
// draft could only be reached through an id the professor had to keep somewhere themselves.
public static class GetMyExams
{
    public sealed record Query(int Page, int PageSize, ExamPackageStatus? Status) : IQuery<PagedList<Response>>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public string Title { get; init; }

        public string Subject { get; init; }

        public string Status { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? PublishedAt { get; init; }

        public int FileCount { get; init; }

        public int DependencyCount { get; init; }

        // Cancelled sittings are counted too: they are still part of the exam's history.
        public int SessionCount { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context, IUserContext userContext)
        : IQueryHandler<Query, PagedList<Response>>
    {
        public async Task<Result<PagedList<Response>>> Handle(Query query, CancellationToken cancellationToken)
        {
            IQueryable<ExamPackage> exams = context.ExamPackages
                .AsNoTracking()
                .Where(e => e.OwnerProfessorId == userContext.UserId);

            if (query.Status is ExamPackageStatus status)
            {
                exams = exams.Where(e => e.Status == status);
            }

            // Newest first, with the id as a tie-break so a page never repeats or drops a row.
            IQueryable<Response> mine = exams
                .OrderByDescending(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .Select(e => new Response
                {
                    Id = e.Id,
                    Title = e.Title.Value,
                    Subject = e.Subject.Value,
                    Status = e.Status.ToString(),
                    CreatedAt = e.CreatedAt,
                    PublishedAt = e.PublishedAt,
                    FileCount = context.ExamFiles.Count(f => f.ExamPackageId == e.Id),
                    DependencyCount = context.ExamDependencies.Count(d => d.ExamPackageId == e.Id),
                    SessionCount = context.ExamSessions.Count(s => s.ExamPackageId == e.Id)
                });

            return await PagedList<Response>.CreateAsync(
                mine,
                query.Page,
                query.PageSize,
                cancellationToken);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            // An unknown status is rejected by the framework with 400 before the handler runs, so
            // a mistyped filter never quietly returns the unfiltered list.
            app.MapGet("exams/mine", async (
                int? page,
                int? pageSize,
                ExamPackageStatus? status,
                IQueryHandler<Query, PagedList<Response>> handler,
                CancellationToken cancellationToken) =>
            {
                var query = new Query(
                    page ?? 1,
                    pageSize ?? PagedList<Response>.DefaultPageSize,
                    status);

                Result<PagedList<Response>> result = await handler.Handle(query, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
