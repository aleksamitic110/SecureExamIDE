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

// Throws a draft away completely: the exam, its files and dependencies, and their bytes in storage.
// A draft is the one state in which that is clean - it has no sittings, so no sealed package, no
// one-time code and no submissions can depend on it. A published exam is never deleted.
public static class DeleteExam
{
    public sealed record Command(Guid ExamId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ExamId).NotEmpty();
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService) : ICommandHandler<Command>
    {
        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            ExamPackage? exam = await context.ExamPackages
                .SingleOrDefaultAsync(
                    e => e.Id == command.ExamId && e.OwnerProfessorId == userContext.UserId,
                    cancellationToken);

            if (exam is null)
            {
                return Result.Failure(ExamErrors.NotFound(command.ExamId));
            }

            if (exam.Status != ExamPackageStatus.Draft)
            {
                return Result.Failure(ExamErrors.NotDraft(exam.Status));
            }

            // A draft holds a handful of task files and dependencies, so loading them is cheap.
            List<ExamFile> files = await context.ExamFiles
                .Where(f => f.ExamPackageId == exam.Id)
                .ToListAsync(cancellationToken);

            List<ExamDependency> dependencies = await context.ExamDependencies
                .Where(d => d.ExamPackageId == exam.Id)
                .ToListAsync(cancellationToken);

            List<string> objectKeys =
            [
                .. files.Select(f => f.ObjectKey.Value),
                .. dependencies.Select(d => d.ObjectKey.Value)
            ];

            exam.Raise(new ExamPackageDeletedDomainEvent(exam.Id, exam.OwnerProfessorId));

            // The rows are removed explicitly rather than left to the cascade, so the same thing
            // happens whatever the database provider.
            context.ExamFiles.RemoveRange(files);
            context.ExamDependencies.RemoveRange(dependencies);
            context.ExamPackages.Remove(exam);

            await context.SaveChangesAsync(cancellationToken);

            // Database first, storage second: a delete that fails part-way leaves orphans, never a
            // row naming an object that is gone.
            foreach (string objectKey in objectKeys)
            {
                await storageService.DeleteAsync(objectKey, cancellationToken);
            }

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("exams/{examId:guid}", async (
                Guid examId,
                ICommandHandler<Command> handler,
                CancellationToken cancellationToken) =>
            {
                Result result = await handler.Handle(new Command(examId), cancellationToken);

                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
