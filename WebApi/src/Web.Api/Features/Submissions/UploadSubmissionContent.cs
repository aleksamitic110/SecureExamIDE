using System.Security.Cryptography;
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

// Phase one of a submission. The solution and its activity log arrive together, in one request,
// because they are handed in as one unit - never a log on its own, never a solution without one.
//
// Both parts are proxied through the API rather than sent straight to storage the way a dependency
// is. They are exam material: having the server witness the bytes is what makes the recorded
// digests the server's own measurement instead of something the client asserted about its own
// work. Source code and an event log are small, so the size that ruled proxying out for
// toolchains is not a concern here.
public static class UploadSubmissionContent
{
    // Both streams must be seekable: each is read once to hash and once to store.
    public sealed record Command(
        Guid SessionId,
        Stream Solution,
        string SolutionContentType,
        Stream ActivityLog,
        string ActivityLogContentType) : ICommand<Response>;

    public sealed record Response(UploadedPart Solution, UploadedPart ActivityLog);

    public sealed record UploadedPart(string ObjectKey, long SizeBytes, string Sha256);

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

            // Both parts are checked before either is stored, so a request carrying only half a
            // submission leaves nothing behind in storage.
            if (command.Solution.Length == 0)
            {
                return Result.Failure<Response>(SubmissionErrors.EmptySolution);
            }

            if (command.ActivityLog.Length == 0)
            {
                return Result.Failure<Response>(SubmissionErrors.EmptyActivityLog);
            }

            UploadedPart solution = await StoreAsync(
                command.Solution,
                command.SolutionContentType,
                SubmissionObjectKeys.NewSolutionKey(command.SessionId, userContext.UserId),
                cancellationToken);

            UploadedPart activityLog = await StoreAsync(
                command.ActivityLog,
                command.ActivityLogContentType,
                SubmissionObjectKeys.NewActivityLogKey(command.SessionId, userContext.UserId),
                cancellationToken);

            return new Response(solution, activityLog);
        }

        private async Task<UploadedPart> StoreAsync(
            Stream content,
            string contentType,
            ObjectKey objectKey,
            CancellationToken cancellationToken)
        {
            byte[] hash = await SHA256.HashDataAsync(content, cancellationToken);
            content.Position = 0;

            long sizeBytes = content.Length;
            string sha256 = Convert.ToHexStringLower(hash);

            Result<ContentType> contentTypeResult = ContentType.Create(contentType);
            ContentType resolvedContentType = contentTypeResult.IsSuccess
                ? contentTypeResult.Value
                : ContentType.Default;

            // The digest is stored with the object, so CreateSubmission reads it back from storage
            // rather than trusting whatever the client sends at commit.
            await storageService.PutAsync(
                objectKey.Value,
                content,
                resolvedContentType.Value,
                sha256,
                cancellationToken);

            return new UploadedPart(objectKey.Value, sizeBytes, sha256);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            // Two named form files. A request missing either one is rejected by the framework with
            // 400 before the handler runs.
            app.MapPost("sessions/{sessionId:guid}/submissions/content", async (
                Guid sessionId,
                IFormFile solution,
                IFormFile activityLog,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                await using Stream solutionContent = solution.OpenReadStream();
                await using Stream activityLogContent = activityLog.OpenReadStream();

                var command = new Command(
                    sessionId,
                    solutionContent,
                    ContentTypeOf(solution),
                    activityLogContent,
                    ContentTypeOf(activityLog));

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .DisableAntiforgery()
            .HasPermission(Permissions.SubmissionsSubmit)
            .WithTags(Tags.Submissions);
        }

        private static string ContentTypeOf(IFormFile file) =>
            string.IsNullOrWhiteSpace(file.ContentType) ? DefaultContentType : file.ContentType;

        private const string DefaultContentType = "application/octet-stream";
    }
}
