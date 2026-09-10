using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;
using Web.Api.Features.ExamSessions;

namespace Web.Api.Features.Submissions;

// How a professor collects the work handed in for one sitting of their own exam: who submitted,
// from which bound machine, when, and the digests the server measured on arrival. The files
// themselves come from GetSubmissionDownload, one submission at a time.
public static class GetSessionSubmissions
{
    public sealed record Query(Guid SessionId, int Page, int PageSize) : IQuery<PagedList<Response>>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public Guid StudentId { get; init; }

        public string StudentFirstName { get; init; }

        public string StudentLastName { get; init; }

        public string? StudentIndexNumber { get; init; }

        public string StudentEmail { get; init; }

        // The machine the work came from, as the student named it when binding it to the account.
        public string DeviceName { get; init; }

        public DateTime SubmittedAt { get; init; }

        // The end of a sitting is deliberately not enforced - a student uploads whenever a
        // connection returns - so lateness is reported for the professor to judge, not refused.
        public bool SubmittedAfterSessionEnded { get; init; }

        public long SolutionSizeBytes { get; init; }

        public string SolutionSha256 { get; init; }

        public long ActivityLogSizeBytes { get; init; }

        public string ActivityLogSha256 { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context, IUserContext userContext)
        : IQueryHandler<Query, PagedList<Response>>
    {
        public async Task<Result<PagedList<Response>>> Handle(Query query, CancellationToken cancellationToken)
        {
            // Another professor's sitting is reported as missing rather than forbidden, so session
            // ids cannot be probed. A cancelled sitting is still listed: whatever was handed in
            // before it was cancelled is still the professor's to see.
            DateTime? endsAt = await (
                from session in context.ExamSessions.AsNoTracking()
                join exam in context.ExamPackages on session.ExamPackageId equals exam.Id
                where session.Id == query.SessionId && exam.OwnerProfessorId == userContext.UserId
                select (DateTime?)session.EndsAt)
                .SingleOrDefaultAsync(cancellationToken);

            if (endsAt is null)
            {
                return Result.Failure<PagedList<Response>>(ExamSessionErrors.NotFound(query.SessionId));
            }

            DateTime sessionEndsAt = endsAt.Value;

            // Ordered by arrival, with the id as a tie-break so a page never repeats or drops a row.
            IQueryable<Response> submissions =
                from submission in context.Submissions.AsNoTracking()
                join student in context.Users on submission.StudentId equals student.Id
                join device in context.DeviceCredentials on submission.DeviceCredentialId equals device.Id
                where submission.ExamSessionId == query.SessionId
                orderby submission.SubmittedAt, submission.Id
                select new Response
                {
                    Id = submission.Id,
                    StudentId = student.Id,
                    StudentFirstName = student.FirstName.Value,
                    StudentLastName = student.LastName.Value,
                    StudentIndexNumber = student.IndexNumber == null ? null : student.IndexNumber.Value,
                    StudentEmail = student.Email.Value,
                    DeviceName = device.DeviceName.Value,
                    SubmittedAt = submission.SubmittedAt,
                    SubmittedAfterSessionEnded = submission.SubmittedAt > sessionEndsAt,
                    SolutionSizeBytes = submission.SolutionSizeBytes,
                    SolutionSha256 = submission.SolutionSha256.Value,
                    ActivityLogSizeBytes = submission.ActivityLogSizeBytes,
                    ActivityLogSha256 = submission.ActivityLogSha256.Value
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
            app.MapGet("sessions/{sessionId:guid}/submissions", async (
                Guid sessionId,
                int? page,
                int? pageSize,
                IQueryHandler<Query, PagedList<Response>> handler,
                CancellationToken cancellationToken) =>
            {
                var query = new Query(
                    sessionId,
                    page ?? 1,
                    pageSize ?? PagedList<Response>.DefaultPageSize);

                Result<PagedList<Response>> result = await handler.Handle(query, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.SubmissionsReview)
            .WithTags(Tags.Submissions);
        }
    }
}
