using Microsoft.EntityFrameworkCore;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;
using Web.Api.Features.Exams;

namespace Web.Api.Features.ExamSessions;

// How a student finds the sitting they are registered for, and therefore which package to
// download. Sessions of an unpublished exam are invisible, the same way the catalog hides drafts.
public static class GetExamSessions
{
    public sealed record Query(Guid ExamId, int Page, int PageSize) : IQuery<PagedList<Response>>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public Guid ExamId { get; init; }

        public DateTime StartsAt { get; init; }

        public DateTime EndsAt { get; init; }

        public long PackageSizeBytes { get; init; }

        public string PackageSha256 { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context)
        : IQueryHandler<Query, PagedList<Response>>
    {
        public async Task<Result<PagedList<Response>>> Handle(Query query, CancellationToken cancellationToken)
        {
            bool examIsPublished = await context.ExamPackages
                .AnyAsync(
                    e => e.Id == query.ExamId && e.Status == ExamPackageStatus.Published,
                    cancellationToken);

            if (!examIsPublished)
            {
                return Result.Failure<PagedList<Response>>(ExamErrors.NotFound(query.ExamId));
            }

            // Cancelled sittings are dropped rather than flagged: a student has nothing to do with
            // one, and its package is no longer downloadable.
            IQueryable<Response> sessions = context.ExamSessions
                .AsNoTracking()
                .Where(s => s.ExamPackageId == query.ExamId && s.IsActive)
                .OrderBy(s => s.StartsAt)
                .ThenBy(s => s.Id)
                .Select(s => new Response
                {
                    Id = s.Id,
                    ExamId = s.ExamPackageId,
                    StartsAt = s.StartsAt,
                    EndsAt = s.EndsAt,
                    PackageSizeBytes = s.PackageSizeBytes,
                    PackageSha256 = s.PackageSha256.Value
                });

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
            app.MapGet("exams/{examId:guid}/sessions", async (
                Guid examId,
                int? page,
                int? pageSize,
                IQueryHandler<Query, PagedList<Response>> handler,
                CancellationToken cancellationToken) =>
            {
                var query = new Query(
                    examId,
                    page ?? 1,
                    pageSize ?? PagedList<Response>.DefaultPageSize);

                Result<PagedList<Response>> result = await handler.Handle(query, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsRead)
            .WithTags(Tags.ExamSessions);
        }
    }
}
