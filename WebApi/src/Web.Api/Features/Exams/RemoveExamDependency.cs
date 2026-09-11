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

// Takes a dependency attached by mistake back out of a draft, and deletes its bytes from storage.
// For a toolchain that can be gigabytes, so leaving it behind as an orphan would not be harmless
// for long. As with files, deleting is safe only because a draft's dependencies can have been
// downloaded by nobody - students see published exams alone.
public static class RemoveExamDependency
{
    public sealed record Command(Guid ExamId, Guid DependencyId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ExamId).NotEmpty();
            RuleFor(c => c.DependencyId).NotEmpty();
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

            ExamDependency? dependency = await context.ExamDependencies
                .SingleOrDefaultAsync(
                    d => d.Id == command.DependencyId && d.ExamPackageId == command.ExamId,
                    cancellationToken);

            if (dependency is null)
            {
                return Result.Failure(ExamErrors.DependencyNotFound(command.DependencyId));
            }

            string objectKey = dependency.ObjectKey.Value;

            dependency.Raise(new ExamDependencyRemovedDomainEvent(dependency.Id, dependency.ExamPackageId));
            context.ExamDependencies.Remove(dependency);

            await context.SaveChangesAsync(cancellationToken);

            // Database first, storage second: a failed delete leaves an orphan, never a row naming
            // an object that is gone.
            await storageService.DeleteAsync(objectKey, cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("exams/{examId:guid}/dependencies/{dependencyId:guid}", async (
                Guid examId,
                Guid dependencyId,
                ICommandHandler<Command> handler,
                CancellationToken cancellationToken) =>
            {
                Result result = await handler.Handle(new Command(examId, dependencyId), cancellationToken);

                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
