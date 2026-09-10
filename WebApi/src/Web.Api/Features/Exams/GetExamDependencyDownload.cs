using Microsoft.EntityFrameworkCore;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.Storage;
using Web.Api.Database;

namespace Web.Api.Features.Exams;

// Hands the student a short-lived URL straight to object storage for one dependency - the mirror
// of CreateExamDependencyUploadUrl. A toolchain is hundreds of megabytes, which is exactly why its
// bytes never pass through the API in either direction.
public static class GetExamDependencyDownload
{
    public sealed record Query(Guid DependencyId) : IQuery<Response>;

    // The URL stays a string: it is produced by the storage client and handed to the caller
    // untouched, and response DTOs in this codebase keep primitive types so the JSON contract is
    // plain. Parsing into Uri only to serialise it back would be work for nothing.
#pragma warning disable CA1054
    public sealed record Response(
        Guid DependencyId,
        string Name,
        string Version,
        string ContentType,
        long SizeBytes,
        string DownloadUrl,
        DateTime ExpiresAt);
#pragma warning restore CA1054

    internal sealed class Handler(
        ApplicationDbContext context,
        IStorageService storageService,
        IDateTimeProvider dateTimeProvider) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query, CancellationToken cancellationToken)
        {
            // A dependency of a draft is reported as missing, so nothing leaks out of an exam before
            // it is published.
            DependencyToDownload? dependency = await (
                from stored in context.ExamDependencies.AsNoTracking()
                join exam in context.ExamPackages on stored.ExamPackageId equals exam.Id
                where stored.Id == query.DependencyId && exam.Status == ExamPackageStatus.Published
                select new DependencyToDownload(
                    stored.Name.Value,
                    stored.Version.Value,
                    stored.ContentType.Value,
                    stored.ObjectKey.Value,
                    stored.SizeBytes))
                .SingleOrDefaultAsync(cancellationToken);

            if (dependency is null)
            {
                return Result.Failure<Response>(ExamErrors.DependencyNotFound(query.DependencyId));
            }

            string downloadUrl = await storageService.CreatePresignedDownloadUrlAsync(
                dependency.ObjectKey, UrlLifetime, cancellationToken);

            return new Response(
                query.DependencyId,
                dependency.Name,
                dependency.Version,
                dependency.ContentType,
                dependency.SizeBytes,
                downloadUrl,
                dateTimeProvider.UtcNow.Add(UrlLifetime));
        }

        private sealed record DependencyToDownload(
            string Name,
            string Version,
            string ContentType,
            string ObjectKey,
            long SizeBytes);

        // As long as the upload link, and for the same reason: a multi-gigabyte toolchain on a home
        // connection. Storage checks the expiry when a request starts, so a download already under
        // way finishes; a client that has to resume after the link lapsed simply asks again.
        private static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(2);
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("dependencies/{dependencyId:guid}/download", async (
                Guid dependencyId,
                IQueryHandler<Query, Response> handler,
                CancellationToken cancellationToken) =>
            {
                Result<Response> result = await handler.Handle(new Query(dependencyId), cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsRead)
            .WithTags(Tags.Exams);
        }
    }
}
