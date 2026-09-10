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

// Phase one for dependencies, and the one place this project deliberately breaks with the proxied
// upload used for exam files. A dependency is a compiler or an SDK - hundreds of megabytes of
// publicly available archive - and pushing that through the API would buffer it to disk, read it
// twice and hold a request open for minutes. Here the server only mints the key and signs a URL;
// the bytes go straight to object storage.
//
// Exam files stay proxied on purpose: they are small, and having the server witness the bytes is
// what lets it compute their SHA-256 itself.
public static class CreateExamDependencyUploadUrl
{
    public sealed record Command(Guid ExamId) : ICommand<Response>;

    // The URL is a capability to write to exactly one server-chosen key, and only until it expires.
#pragma warning disable CA1054
    public sealed record Response(string ObjectKey, string UploadUrl, DateTime ExpiresAt);
#pragma warning restore CA1054

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService,
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            ExamPackageStatus? status = await context.ExamPackages
                .Where(e => e.Id == command.ExamId && e.OwnerProfessorId == userContext.UserId)
                .Select(e => (ExamPackageStatus?)e.Status)
                .SingleOrDefaultAsync(cancellationToken);

            // Another professor's exam is reported as missing rather than as forbidden, so exam ids
            // cannot be probed.
            if (status is null)
            {
                return Result.Failure<Response>(ExamErrors.NotFound(command.ExamId));
            }

            if (status != ExamPackageStatus.Draft)
            {
                return Result.Failure<Response>(ExamErrors.NotDraft(status.Value));
            }

            // Still minted by the server. That is what stops a caller writing into another exam's
            // prefix or overwriting an object it does not own - the signature is bound to this key
            // and no other.
            ObjectKey objectKey = ExamObjectKeys.NewDependencyKey(command.ExamId);

            string uploadUrl = await storageService.CreatePresignedUploadUrlAsync(
                objectKey.Value, UrlLifetime, cancellationToken);

            return new Response(objectKey.Value, uploadUrl, dateTimeProvider.UtcNow.Add(UrlLifetime));
        }

        // Generous, because the whole point is that something large is about to be sent over a
        // home connection. A client that runs out of time asks for another URL.
        private static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(2);
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("exams/{examId:guid}/dependencies/upload-url", async (
                Guid examId,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                Result<Response> result = await handler.Handle(new Command(examId), cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
