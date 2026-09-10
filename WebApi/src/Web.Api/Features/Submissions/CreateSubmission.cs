using FluentValidation;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.Storage;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;

namespace Web.Api.Features.Submissions;

// Phase two: records an already-stored solution and activity log as this student's submission for
// the sitting. Each key must have been minted under this student's own prefix and for its own part,
// so one student cannot claim another's upload and a solution cannot stand in for the log. Both
// objects must be in storage before the row is written - a submission is the pair, never half of it.
//
// The request carries keys only. Sizes and digests are read back from storage, where phase one
// recorded them, so the client has no say in what the professor will later be told it sent.
public static class CreateSubmission
{
    public sealed record Command(
        Guid SessionId,
        string SolutionObjectKey,
        string ActivityLogObjectKey) : ICommand<Response>;

    public sealed record Response(
        Guid SubmissionId,
        DateTime SubmittedAt,
        long SolutionSizeBytes,
        string SolutionSha256,
        long ActivityLogSizeBytes,
        string ActivityLogSha256);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SessionId).NotEmpty();

            RuleFor(c => c.SolutionObjectKey)
                .Must(v => ObjectKey.Create(v).IsSuccess)
                .WithMessage($"Solution object key is required and must not exceed {ObjectKey.MaxLength} characters.");

            RuleFor(c => c.ActivityLogObjectKey)
                .Must(v => ObjectKey.Create(v).IsSuccess)
                .WithMessage($"Activity log object key is required and must not exceed {ObjectKey.MaxLength} characters.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService,
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (userContext.DeviceId is null)
            {
                return Result.Failure<Response>(SubmissionErrors.DeviceTokenRequired);
            }

            Result<ObjectKey> solutionKeyResult = ObjectKey.Create(command.SolutionObjectKey);

            if (solutionKeyResult.IsFailure)
            {
                return Result.Failure<Response>(solutionKeyResult.Error);
            }

            Result<ObjectKey> activityLogKeyResult = ObjectKey.Create(command.ActivityLogObjectKey);

            if (activityLogKeyResult.IsFailure)
            {
                return Result.Failure<Response>(activityLogKeyResult.Error);
            }

            Result sessionResult = await SubmissionRules.CheckSessionAsync(
                context, command.SessionId, userContext.UserId, dateTimeProvider, cancellationToken);

            if (sessionResult.IsFailure)
            {
                return Result.Failure<Response>(sessionResult.Error);
            }

            ObjectKey solutionKey = solutionKeyResult.Value;
            ObjectKey activityLogKey = activityLogKeyResult.Value;

            // Without this a student could commit an object another student uploaded, and submit
            // work that is not theirs - or hand in a second copy of the solution in place of the log.
            if (!SubmissionObjectKeys.IsSolutionOf(solutionKey, command.SessionId, userContext.UserId) ||
                !SubmissionObjectKeys.IsActivityLogOf(activityLogKey, command.SessionId, userContext.UserId))
            {
                return Result.Failure<Response>(SubmissionErrors.ObjectKeyNotOwned);
            }

            StoredPart? solution = await StatAsync(solutionKey, cancellationToken);

            if (solution is null)
            {
                return Result.Failure<Response>(SubmissionErrors.SolutionNotUploaded);
            }

            StoredPart? activityLog = await StatAsync(activityLogKey, cancellationToken);

            if (activityLog is null)
            {
                return Result.Failure<Response>(SubmissionErrors.ActivityLogNotUploaded);
            }

            var submission = new Submission
            {
                Id = Guid.NewGuid(),
                ExamSessionId = command.SessionId,
                StudentId = userContext.UserId,
                DeviceCredentialId = userContext.DeviceId.Value,
                SolutionObjectKey = solutionKey,
                SolutionSizeBytes = solution.SizeBytes,
                SolutionSha256 = solution.Sha256,
                ActivityLogObjectKey = activityLogKey,
                ActivityLogSizeBytes = activityLog.SizeBytes,
                ActivityLogSha256 = activityLog.Sha256,
                SubmittedAt = dateTimeProvider.UtcNow
            };

            submission.Raise(new SubmissionCreatedDomainEvent(
                submission.Id, submission.ExamSessionId, submission.StudentId));

            context.Submissions.Add(submission);

            await context.SaveChangesAsync(cancellationToken);

            return new Response(
                submission.Id,
                submission.SubmittedAt,
                submission.SolutionSizeBytes,
                submission.SolutionSha256.Value,
                submission.ActivityLogSizeBytes,
                submission.ActivityLogSha256.Value);
        }

        // What storage reports for one part, or null when there is nothing usable under the key.
        // An object with no recorded digest was not written by phase one, which always records
        // one, so it counts as not uploaded.
        private async Task<StoredPart?> StatAsync(ObjectKey objectKey, CancellationToken cancellationToken)
        {
            StorageObjectInfo? stored = await storageService.StatAsync(objectKey.Value, cancellationToken);

            if (stored?.Sha256 is null)
            {
                return null;
            }

            Result<Sha256Hash> sha256Result = Sha256Hash.Create(stored.Sha256);

            return sha256Result.IsSuccess
                ? new StoredPart(stored.SizeBytes, sha256Result.Value)
                : null;
        }

        private sealed record StoredPart(long SizeBytes, Sha256Hash Sha256);
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string SolutionObjectKey, string ActivityLogObjectKey);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("sessions/{sessionId:guid}/submissions", async (
                Guid sessionId,
                Request request,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(sessionId, request.SolutionObjectKey, request.ActivityLogObjectKey);

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.SubmissionsSubmit)
            .WithTags(Tags.Submissions);
        }
    }
}
