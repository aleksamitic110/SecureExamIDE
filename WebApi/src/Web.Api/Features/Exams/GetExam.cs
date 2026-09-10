using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Exams;

public static class GetExam
{
    public sealed record Query(Guid ExamId) : IQuery<Response>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public string Title { get; init; }

        public string Description { get; init; }

        public string Subject { get; init; }

        public ExamPackageStatus Status { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? PublishedAt { get; init; }

        public IReadOnlyCollection<FileResponse> Files { get; init; } = [];

        public IReadOnlyCollection<DependencyResponse> Dependencies { get; init; } = [];
    }

    // Carries no digest: a dependency is a third-party archive the API never sees the bytes of.
    public sealed record DependencyResponse
    {
        public Guid Id { get; init; }

        public string Name { get; init; }

        public string Version { get; init; }

        public string ContentType { get; init; }

        public long SizeBytes { get; init; }
    }

    public sealed record FileResponse
    {
        public Guid Id { get; init; }

        public string FileName { get; init; }

        public string ContentType { get; init; }

        public long SizeBytes { get; init; }

        public string Sha256 { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context, IUserContext userContext)
        : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query, CancellationToken cancellationToken)
        {
            // Scoped to the owner: a draft exam must not be readable by anyone else, least of all a
            // student. The published catalog is a separate, deliberately different read.
            Response? exam = await context.ExamPackages
                .AsNoTracking()
                .Where(e => e.Id == query.ExamId && e.OwnerProfessorId == userContext.UserId)
                .Select(e => new Response
                {
                    Id = e.Id,
                    Title = e.Title.Value,
                    Description = e.Description.Value,
                    Subject = e.Subject.Value,
                    Status = e.Status,
                    CreatedAt = e.CreatedAt,
                    PublishedAt = e.PublishedAt,
                    Files = context.ExamFiles
                        .Where(f => f.ExamPackageId == e.Id)
                        .OrderBy(f => f.CreatedAt)
                        .Select(f => new FileResponse
                        {
                            Id = f.Id,
                            FileName = f.FileName.Value,
                            ContentType = f.ContentType.Value,
                            SizeBytes = f.SizeBytes,
                            Sha256 = f.Sha256.Value
                        })
                        .ToList(),
                    Dependencies = context.ExamDependencies
                        .Where(d => d.ExamPackageId == e.Id)
                        .OrderBy(d => d.CreatedAt)
                        .Select(d => new DependencyResponse
                        {
                            Id = d.Id,
                            Name = d.Name.Value,
                            Version = d.Version.Value,
                            ContentType = d.ContentType.Value,
                            SizeBytes = d.SizeBytes
                        })
                        .ToList()
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (exam is null)
            {
                return Result.Failure<Response>(ExamErrors.NotFound(query.ExamId));
            }

            return exam;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("exams/{examId:guid}", async (
                Guid examId,
                IQueryHandler<Query, Response> handler,
                CancellationToken cancellationToken) =>
            {
                Result<Response> result = await handler.Handle(new Query(examId), cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
