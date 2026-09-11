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

// Corrects a draft's title, description or subject. Only the fields sent are changed, so fixing a
// typo in the title does not require resending the rest. Once published an exam is fixed - the
// catalog has shown it and sittings may already be sealed from it.
public static class UpdateExam
{
    public sealed record Command(Guid ExamId, string? Title, string? Description, string? Subject) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ExamId).NotEmpty();

            RuleFor(c => c)
                .Must(c => c.Title is not null || c.Description is not null || c.Subject is not null)
                .WithName("Request")
                .WithMessage("At least one of title, description or subject must be provided.");

            When(c => c.Title is not null, () =>
                RuleFor(c => c.Title)
                    .Must(v => ExamTitle.Create(v!).IsSuccess)
                    .WithMessage($"Title must not be empty or exceed {ExamTitle.MaxLength} characters."));

            When(c => c.Description is not null, () =>
                RuleFor(c => c.Description)
                    .Must(v => ExamDescription.Create(v!).IsSuccess)
                    .WithMessage($"Description must not be empty or exceed {ExamDescription.MaxLength} characters."));

            When(c => c.Subject is not null, () =>
                RuleFor(c => c.Subject)
                    .Must(v => ExamSubject.Create(v!).IsSuccess)
                    .WithMessage($"Subject must not be empty or exceed {ExamSubject.MaxLength} characters."));
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext) : ICommandHandler<Command>
    {
        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            Result<ExamTitle>? title = command.Title is null ? null : ExamTitle.Create(command.Title);

            if (title is { IsFailure: true })
            {
                return Result.Failure(title.Error);
            }

            Result<ExamDescription>? description =
                command.Description is null ? null : ExamDescription.Create(command.Description);

            if (description is { IsFailure: true })
            {
                return Result.Failure(description.Error);
            }

            Result<ExamSubject>? subject = command.Subject is null ? null : ExamSubject.Create(command.Subject);

            if (subject is { IsFailure: true })
            {
                return Result.Failure(subject.Error);
            }

            // Another professor's exam is reported as missing rather than forbidden, so exam ids
            // cannot be probed.
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

            if (title is not null)
            {
                exam.Title = title.Value;
            }

            if (description is not null)
            {
                exam.Description = description.Value;
            }

            if (subject is not null)
            {
                exam.Subject = subject.Value;
            }

            exam.Raise(new ExamPackageUpdatedDomainEvent(exam.Id));

            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string? Title, string? Description, string? Subject);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPatch("exams/{examId:guid}", async (
                Guid examId,
                Request request,
                ICommandHandler<Command> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(examId, request.Title, request.Description, request.Subject);

                Result result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
