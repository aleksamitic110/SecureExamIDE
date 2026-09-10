using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
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

namespace Web.Api.Features.Exams;

// Phase one of the two-phase upload: writes the bytes to object storage and returns the key they
// were written under. It deliberately touches no table. The metadata row is created separately by
// AddExamFile, so the database can never name an object that was not stored first -
// the worst this can leave behind is an uploaded object that is never committed.
public static class UploadExamFileContent
{
    // Content must be seekable: it is read once to hash and once to store.
    public sealed record Command(Guid ExamId, Stream Content, string ContentType) : ICommand<Response>;

    public sealed record Response(string ObjectKey, long SizeBytes, string Sha256);

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            ExamPackageStatus? status = await context.ExamPackages
                .Where(e => e.Id == command.ExamId && e.OwnerProfessorId == userContext.UserId)
                .Select(e => (ExamPackageStatus?)e.Status)
                .SingleOrDefaultAsync(cancellationToken);

            // Another professor's exam is reported as missing rather than as forbidden, so exam ids
            // cannot be probed - the same choice RevokeDevice makes for device ids.
            if (status is null)
            {
                return Result.Failure<Response>(ExamErrors.NotFound(command.ExamId));
            }

            if (status != ExamPackageStatus.Draft)
            {
                return Result.Failure<Response>(ExamErrors.NotDraft(status.Value));
            }

            if (command.Content.Length == 0)
            {
                return Result.Failure<Response>(ExamErrors.EmptyContent);
            }

            // Hashed here, from the bytes actually being stored, so the digest recorded later is the
            // server's own measurement rather than something the caller asserted.
            byte[] hash = await SHA256.HashDataAsync(command.Content, cancellationToken);
            command.Content.Position = 0;

            ObjectKey objectKey = ExamObjectKeys.NewFileKey(command.ExamId);
            long sizeBytes = command.Content.Length;

            // A browser or client may send a malformed or absent media type; fall back rather than
            // reject, since the bytes themselves are what matter here.
            Result<ContentType> contentTypeResult = ContentType.Create(command.ContentType);
            ContentType contentType = contentTypeResult.IsSuccess
                ? contentTypeResult.Value
                : ContentType.Default;

            // IStorageService stays string-based because it mirrors the S3 API; the value object is
            // unwrapped only at that boundary.
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
            app.MapPost("exams/{examId:guid}/files/content", async (
                Guid examId,
                IFormFile file,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                await using Stream content = file.OpenReadStream();

                var command = new Command(
                    examId,
                    content,
                    string.IsNullOrWhiteSpace(file.ContentType)
                        ? DefaultContentType
                        : file.ContentType);

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .DisableAntiforgery()
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }

        private const string DefaultContentType = "application/octet-stream";
    }
}
