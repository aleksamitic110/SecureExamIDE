using System.Security.Cryptography;
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
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Exams;

namespace Web.Api.Features.Submissions;

// Phase one of the submission, proxied through the API rather than sent straight to storage the
// way a dependency is. A solution is exam material: having the server witness the bytes is what
// makes the recorded digest the server's own measurement instead of something the client asserted
// about its own work. Solutions are source code, so the size that ruled proxying out for
// toolchains is not a concern here.
public static class UploadSolutionContent
{
    // Content must be seekable: it is read once to hash and once to store.
    public sealed record Command(Guid SessionId, Stream Content, string ContentType) : ICommand<Response>;

    public sealed record Response(string ObjectKey, long SizeBytes, string Sha256);

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

            Result sessionResult = await SubmissionRules.CheckSessionAsync(
                context, command.SessionId, userContext.UserId, dateTimeProvider, cancellationToken);

            if (sessionResult.IsFailure)
            {
                return Result.Failure<Response>(sessionResult.Error);
            }

            if (command.Content.Length == 0)
            {
                return Result.Failure<Response>(SubmissionErrors.EmptyContent);
            }

            byte[] hash = await SHA256.HashDataAsync(command.Content, cancellationToken);
            command.Content.Position = 0;

            ObjectKey objectKey = SubmissionObjectKeys.NewSolutionKey(command.SessionId, userContext.UserId);
            long sizeBytes = command.Content.Length;

            Result<ContentType> contentTypeResult = ContentType.Create(command.ContentType);
            ContentType contentType = contentTypeResult.IsSuccess
                ? contentTypeResult.Value
                : ContentType.Default;

            await storageService.PutAsync(
                objectKey.Value,
                command.Content,
                contentType.Value,
                cancellationToken);

            return new Response(objectKey.Value, sizeBytes, Convert.ToHexStringLower(hash));
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("sessions/{sessionId:guid}/submissions/content", async (
                Guid sessionId,
                IFormFile file,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                await using Stream content = file.OpenReadStream();

                var command = new Command(
                    sessionId,
                    content,
                    string.IsNullOrWhiteSpace(file.ContentType)
                        ? DefaultContentType
                        : file.ContentType);

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .DisableAntiforgery()
            .HasPermission(Permissions.SubmissionsSubmit)
            .WithTags(Tags.Submissions);
        }

        private const string DefaultContentType = "application/octet-stream";
    }
}
