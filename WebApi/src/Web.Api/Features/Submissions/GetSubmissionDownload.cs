using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.Storage;
using Web.Api.Database;

namespace Web.Api.Features.Submissions;

// Hands the professor short-lived URLs for both halves of one submission, straight to object
// storage the way the package download does. The digests come with them, so whatever fetches the
// files can check they are exactly the bytes the server received from the student's laptop.
public static class GetSubmissionDownload
{
    public sealed record Query(Guid SubmissionId) : IQuery<Response>;

    // The URLs stay strings: they are produced by the storage client and handed to the caller
    // untouched, and response DTOs in this codebase keep primitive types so the JSON contract is
    // plain. Parsing into Uri only to serialise it back would be work for nothing.
#pragma warning disable CA1054
    public sealed record Response(
        Guid SubmissionId,
        string SolutionUrl,
        string ActivityLogUrl,
        DateTime ExpiresAt,
        long SolutionSizeBytes,
        string SolutionSha256,
        long ActivityLogSizeBytes,
        string ActivityLogSha256);
#pragma warning restore CA1054

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService,
        IDateTimeProvider dateTimeProvider) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query, CancellationToken cancellationToken)
        {
            // Only the professor who owns the exam may see the work handed in for it. Anyone else's
            // submission is reported as missing, so submission ids cannot be probed.
            StoredSubmission? submission = await (
                from stored in context.Submissions.AsNoTracking()
                join session in context.ExamSessions on stored.ExamSessionId equals session.Id
                join exam in context.ExamPackages on session.ExamPackageId equals exam.Id
                where stored.Id == query.SubmissionId && exam.OwnerProfessorId == userContext.UserId
                select new StoredSubmission(
                    stored.SolutionObjectKey.Value,
                    stored.SolutionSizeBytes,
                    stored.SolutionSha256.Value,
                    stored.ActivityLogObjectKey.Value,
                    stored.ActivityLogSizeBytes,
                    stored.ActivityLogSha256.Value))
                .SingleOrDefaultAsync(cancellationToken);

            if (submission is null)
            {
                return Result.Failure<Response>(SubmissionErrors.NotFound(query.SubmissionId));
            }

            string solutionUrl = await storageService.CreatePresignedDownloadUrlAsync(
                submission.SolutionObjectKey, UrlLifetime, cancellationToken);

            string activityLogUrl = await storageService.CreatePresignedDownloadUrlAsync(
                submission.ActivityLogObjectKey, UrlLifetime, cancellationToken);

            return new Response(
                query.SubmissionId,
                solutionUrl,
                activityLogUrl,
                dateTimeProvider.UtcNow.Add(UrlLifetime),
                submission.SolutionSizeBytes,
                submission.SolutionSha256,
                submission.ActivityLogSizeBytes,
                submission.ActivityLogSha256);
        }

        private sealed record StoredSubmission(
            string SolutionObjectKey,
            long SolutionSizeBytes,
            string SolutionSha256,
            string ActivityLogObjectKey,
            long ActivityLogSizeBytes,
            string ActivityLogSha256);

        // A professor fetches a submission to look at it now, the files are small, and the link
        // names one student's work - so it lives a quarter of an hour, not the hour a package link
        // gets to survive a slow home connection. A professor who runs out of time asks again.
        private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(15);
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("submissions/{submissionId:guid}/download", async (
                Guid submissionId,
                IQueryHandler<Query, Response> handler,
                CancellationToken cancellationToken) =>
            {
                Result<Response> result = await handler.Handle(new Query(submissionId), cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.SubmissionsReview)
            .WithTags(Tags.Submissions);
        }
    }
}
