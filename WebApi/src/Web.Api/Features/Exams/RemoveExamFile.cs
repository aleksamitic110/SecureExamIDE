using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.Storage;
using Web.Api.Database;

namespace Web.Api.Features.Exams;

// Takes a task file uploaded by mistake back out of a draft, and deletes its bytes from storage.
// Deleting the object is safe only because the exam is a draft: sittings - and so sealed
// packages built from these files - exist for published exams alone, so nothing else can
// reference it.
public static class RemoveExamFile
{
    public sealed record Command(Guid ExamId, Guid FileId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ExamId).NotEmpty();
            RuleFor(c => c.FileId).NotEmpty();
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService) : ICommandHandler<Command>
    {
        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            ExamPackageStatus? status = await context.ExamPackages
                .Where(e => e.Id == command.ExamId && e.OwnerProfessorId == userContext.UserId)
                .Select(e => (ExamPackageStatus?)e.Status)
                .SingleOrDefaultAsync(cancellationToken);

            if (status is null)
            {
                return Result.Failure(ExamErrors.NotFound(command.ExamId));
            }

            if (status != ExamPackageStatus.Draft)
            {
                return Result.Failure(ExamErrors.NotDraft(status.Value));
            }

            // Looked up within this exam, so a file id from another exam is simply not found here.
            ExamFile? file = await context.ExamFiles
                .SingleOrDefaultAsync(
                    f => f.Id == command.FileId && f.ExamPackageId == command.ExamId,
                    cancellationToken);

            if (file is null)
            {
                return Result.Failure(ExamErrors.FileNotFound(command.FileId));
            }

            string objectKey = file.ObjectKey.Value;

            file.Raise(new ExamFileRemovedDomainEvent(file.Id, file.ExamPackageId));
            context.ExamFiles.Remove(file);

            await context.SaveChangesAsync(cancellationToken);

            // Database first, storage second - the reverse of an upload, for the same reason. If
            // the delete below fails, what is left is an object no row names, the harmless orphan
            // this project already accepts; the other order could leave a row naming an object
            // that no longer exists.
            await storageService.DeleteAsync(objectKey, cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("exams/{examId:guid}/files/{fileId:guid}", async (
                Guid examId,
                Guid fileId,
                ICommandHandler<Command> handler,
                CancellationToken cancellationToken) =>
            {
                Result result = await handler.Handle(new Command(examId, fileId), cancellationToken);

                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
