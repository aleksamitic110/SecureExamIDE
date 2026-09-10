using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Exams;

// Publishing is the point of no return for authoring: it makes the exam visible to students and
// closes it to further uploads, which is why every slice that writes content refuses to run unless
// the exam is still a draft. Sealing is deliberately not done here - it happens per session, in
// CreateExamSession, so two sittings of the same exam never share a one-time code.
public static class PublishExam
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
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command>
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
                return Result.Failure(ExamErrors.CannotPublish(exam.Status));
            }

            bool hasFiles = await context.ExamFiles
                .AnyAsync(f => f.ExamPackageId == exam.Id, cancellationToken);

            // Dependencies are not required: an exam whose tasks need no toolchain beyond what the
            // student already has is perfectly valid. Files are, since they are the exam itself.
            if (!hasFiles)
            {
                return Result.Failure(ExamErrors.NoFiles);
            }

            exam.Status = ExamPackageStatus.Published;
            exam.PublishedAt = dateTimeProvider.UtcNow;

            exam.Raise(new ExamPackagePublishedDomainEvent(exam.Id, exam.OwnerProfessorId));

            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPatch("exams/{examId:guid}/publish", async (
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
