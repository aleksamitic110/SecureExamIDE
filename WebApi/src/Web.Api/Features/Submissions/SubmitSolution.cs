using FluentValidation;
using Microsoft.EntityFrameworkCore;
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

// Phase two: records an already-stored solution as this student's submission for the sitting. The
// key must have been minted under this student's own prefix, so one student cannot claim another's
// uploaded work, and the object must be in storage before any row is written.
public static class SubmitSolution
{
    public sealed record Command(Guid SessionId, string ObjectKey, string Sha256) : ICommand<Response>;

    public sealed record Response(Guid SubmissionId, DateTime SubmittedAt, long SizeBytes, string Sha256);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SessionId).NotEmpty();

            RuleFor(c => c.ObjectKey)
                .Must(v => ObjectKey.Create(v).IsSuccess)
                .WithMessage($"Object key is required and must not exceed {ObjectKey.MaxLength} characters.");

            RuleFor(c => c.Sha256)
                .Must(v => Sha256Hash.Create(v).IsSuccess)
                .WithMessage("SHA-256 must be 64 lowercase hexadecimal characters.");
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

            Result<ObjectKey> objectKeyResult = ObjectKey.Create(command.ObjectKey);

            if (objectKeyResult.IsFailure)
            {
                return Result.Failure<Response>(objectKeyResult.Error);
            }

            Result<Sha256Hash> sha256Result = Sha256Hash.Create(command.Sha256);

            if (sha256Result.IsFailure)
            {
                return Result.Failure<Response>(sha256Result.Error);
            }

            Result sessionResult = await SubmissionRules.CheckSessionAsync(
                context, command.SessionId, userContext.UserId, dateTimeProvider, cancellationToken);

            if (sessionResult.IsFailure)
            {
                return Result.Failure<Response>(sessionResult.Error);
            }

            ObjectKey objectKey = objectKeyResult.Value;

            // Without this a student could commit an object another student uploaded, and submit
            // work that is not theirs.
            if (!SubmissionObjectKeys.BelongsTo(objectKey, command.SessionId, userContext.UserId))
            {
                return Result.Failure<Response>(SubmissionErrors.ObjectKeyNotOwned);
            }

            StorageObjectInfo? stored = await storageService.StatAsync(objectKey.Value, cancellationToken);

            if (stored is null)
            {
                return Result.Failure<Response>(SubmissionErrors.SolutionNotUploaded);
            }

            var submission = new Submission
            {
                Id = Guid.NewGuid(),
                ExamSessionId = command.SessionId,
                StudentId = userContext.UserId,
                DeviceCredentialId = userContext.DeviceId.Value,
                SolutionObjectKey = objectKey,

                // Read back from the store, so the row cannot misdescribe the object.
                SizeBytes = stored.SizeBytes,
                Sha256 = sha256Result.Value,
                SubmittedAt = dateTimeProvider.UtcNow
            };

            submission.Raise(new SubmissionCreatedDomainEvent(
                submission.Id, submission.ExamSessionId, submission.StudentId));

            context.Submissions.Add(submission);

            await context.SaveChangesAsync(cancellationToken);

            return new Response(
                submission.Id,
                submission.SubmittedAt,
                submission.SizeBytes,
                submission.Sha256.Value);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string ObjectKey, string Sha256);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("sessions/{sessionId:guid}/submissions", async (
                Guid sessionId,
                Request request,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(sessionId, request.ObjectKey, request.Sha256);

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.SubmissionsSubmit)
            .WithTags(Tags.Submissions);
        }
    }
}
