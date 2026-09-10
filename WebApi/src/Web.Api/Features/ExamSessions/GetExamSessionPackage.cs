using Microsoft.EntityFrameworkCore;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;

namespace Web.Api.Features.ExamSessions;

// Hands the student short-lived URLs straight to object storage rather than streaming the bytes
// through the API. Handing out the encrypted package early is the intended flow, not a leak: it is
// useless until the professor reads out the code on exam day, which is exactly what lets a student
// prepare at home and then sit the exam with no network.
public static class GetExamSessionPackage
{
    public sealed record Query(Guid SessionId) : IQuery<Response>;

    // The URLs stay strings: they are produced by the storage client and handed to the caller
    // untouched, and response DTOs in this codebase keep primitive types so the JSON contract is
    // plain. Parsing into Uri only to serialise it back would be work for nothing.
#pragma warning disable CA1054
    public sealed record Response(
        Guid SessionId,
        string PackageUrl,
        string HeaderUrl,
        DateTime ExpiresAt,
        long PackageSizeBytes,
        string PackageSha256);
#pragma warning restore CA1054

    internal sealed class Handler(
        ApplicationDbContext context,
        IStorageService storageService,
        IDateTimeProvider dateTimeProvider) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query, CancellationToken cancellationToken)
        {
            SessionPackage? session = await context.ExamSessions
                .AsNoTracking()
                .Where(s => s.Id == query.SessionId)
                .Join(
                    context.ExamPackages,
                    s => s.ExamPackageId,
                    e => e.Id,
                    (s, e) => new SessionPackage(
                        s.Id,
                        s.IsActive,
                        e.Status,
                        s.PackageObjectKey.Value,
                        s.HeaderObjectKey.Value,
                        s.PackageSizeBytes,
                        s.PackageSha256.Value))
                .SingleOrDefaultAsync(cancellationToken);

            // A session of an unpublished exam is reported as missing, so pulling an exam back out
            // of the catalog also hides every sitting of it.
            if (session is null || session.ExamStatus != ExamPackageStatus.Published)
            {
                return Result.Failure<Response>(ExamSessionErrors.NotFound(query.SessionId));
            }

            if (!session.IsActive)
            {
                return Result.Failure<Response>(ExamSessionErrors.Cancelled);
            }

            string packageUrl = await storageService.CreatePresignedDownloadUrlAsync(
                session.PackageObjectKey, UrlLifetime, cancellationToken);

            string headerUrl = await storageService.CreatePresignedDownloadUrlAsync(
                session.HeaderObjectKey, UrlLifetime, cancellationToken);

            return new Response(
                session.Id,
                packageUrl,
                headerUrl,
                dateTimeProvider.UtcNow.Add(UrlLifetime),
                session.PackageSizeBytes,
                session.PackageSha256);
        }

        private sealed record SessionPackage(
            Guid Id,
            bool IsActive,
            ExamPackageStatus ExamStatus,
            string PackageObjectKey,
            string HeaderObjectKey,
            long PackageSizeBytes,
            string PackageSha256);

        // Long enough for a slow home connection to finish, short enough that a copied link is not
        // a lasting way in. A client that runs out of time simply asks again.
        private static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(1);
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("sessions/{sessionId:guid}/package", async (
                Guid sessionId,
                IQueryHandler<Query, Response> handler,
                CancellationToken cancellationToken) =>
            {
                Result<Response> result = await handler.Handle(new Query(sessionId), cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsRead)
            .WithTags(Tags.ExamSessions);
        }
    }
}
