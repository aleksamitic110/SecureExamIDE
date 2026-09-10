using Microsoft.EntityFrameworkCore;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Exams;

// What a student has to install before the exam: the compilers, SDKs and libraries the professor
// attached. The catalog only reports how many there are and how large; this names them, so the
// client can fetch each one through GetExamDependencyDownload while it is still online at home.
// A draft's dependencies are invisible, the same way the catalog hides drafts.
public static class GetExamDependencies
{
    public sealed record Query(Guid ExamId, int Page, int PageSize) : IQuery<PagedList<Response>>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public string Name { get; init; }

        public string Version { get; init; }

        public string ContentType { get; init; }

        // No digest: dependencies are public archives the server never hashes. The size is what a
        // client can check a finished download against.
        public long SizeBytes { get; init; }
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

            // In the order the professor attached them, with the id as a tie-break so a page never
            // repeats or drops a row.
            IQueryable<Response> dependencies = context.ExamDependencies
                .AsNoTracking()
                .Where(d => d.ExamPackageId == query.ExamId)
                .OrderBy(d => d.CreatedAt)
                .ThenBy(d => d.Id)
                .Select(d => new Response
                {
                    Id = d.Id,
                    Name = d.Name.Value,
                    Version = d.Version.Value,
                    ContentType = d.ContentType.Value,
                    SizeBytes = d.SizeBytes
                });

            return await PagedList<Response>.CreateAsync(
                dependencies,
                query.Page,
                query.PageSize,
                cancellationToken);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("exams/{examId:guid}/dependencies", async (
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
            .WithTags(Tags.Exams);
        }
    }
}
