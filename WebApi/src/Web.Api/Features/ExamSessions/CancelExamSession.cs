using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.ExamSessions;

// Takes a sitting away from students: it drops out of their list, its package can no longer be
// fetched, and no new work is accepted for it. Every one of those rules already keys off IsActive,
// so cancelling is only a matter of clearing it.
//
// Anything handed in before the cancellation stays exactly where it is and remains in the
// professor's review - a submission is evidence and never disappears. Cancelling a sitting that
// has already ended is allowed on purpose: it is also how a professor stops accepting late uploads.
// There is no way back; a professor who cancelled by mistake schedules a new sitting, which gets a
// fresh one-time code anyway.
public static class CancelExamSession
{
    public sealed record Command(Guid SessionId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SessionId).NotEmpty();
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext) : ICommandHandler<Command>
    {
        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            // Another professor's sitting is reported as missing rather than forbidden, so session
            // ids cannot be probed.
            ExamSession? session = await (
                from candidate in context.ExamSessions
                join exam in context.ExamPackages on candidate.ExamPackageId equals exam.Id
                where candidate.Id == command.SessionId && exam.OwnerProfessorId == userContext.UserId
                select candidate)
                .SingleOrDefaultAsync(cancellationToken);

            if (session is null)
            {
                return Result.Failure(ExamSessionErrors.NotFound(command.SessionId));
            }

            if (!session.IsActive)
            {
                return Result.Failure(ExamSessionErrors.Cancelled);
            }

            session.IsActive = false;

            session.Raise(new ExamSessionCancelledDomainEvent(session.Id, session.ExamPackageId));

            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPatch("sessions/{sessionId:guid}/cancel", async (
                Guid sessionId,
                ICommandHandler<Command> handler,
                CancellationToken cancellationToken) =>
            {
                Result result = await handler.Handle(new Command(sessionId), cancellationToken);

                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.ExamSessions);
        }
    }
}
