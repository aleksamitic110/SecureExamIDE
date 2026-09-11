using Microsoft.EntityFrameworkCore;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Exams;

namespace Web.Api.Features.Submissions;

// Shared by both phases so the two cannot disagree about when a session will accept work.
internal static class SubmissionRules
{
    public static async Task<Result> CheckSessionAsync(
        ApplicationDbContext context,
        Guid sessionId,
        Guid studentId,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        SessionState? session = await context.ExamSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Join(
                context.ExamPackages,
                s => s.ExamPackageId,
                e => e.Id,
                (s, e) => new SessionState(s.IsActive, e.Status, s.StartsAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (session is null || session.ExamStatus != ExamPackageStatus.Published)
        {
            return Result.Failure(ExamSessionErrors.NotFound(sessionId));
        }

        if (!session.IsActive)
        {
            return Result.Failure(ExamSessionErrors.Cancelled);
        }

        // Nothing can have been produced before the sitting opened. The end is deliberately not
        // checked: a student works offline and uploads whenever a connection returns, which may be
        // hours after the exam finished. How late it arrived is recorded, not refused.
        if (dateTimeProvider.UtcNow < session.StartsAt)
        {
            return Result.Failure(SubmissionErrors.SessionNotStarted);
        }

        bool alreadySubmitted = await context.Submissions.AnyAsync(
            s => s.ExamSessionId == sessionId && s.StudentId == studentId,
            cancellationToken);

        return alreadySubmitted
            ? Result.Failure(SubmissionErrors.AlreadySubmitted)
            : Result.Success();
    }

    private sealed record SessionState(bool IsActive, ExamPackageStatus ExamStatus, DateTime StartsAt);
}
