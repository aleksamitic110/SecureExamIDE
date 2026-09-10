using FluentValidation;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Exams;

public static class CreateExam
{
    public sealed record Command(string Title, string Description, string Subject) : ICommand<Guid>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Title)
                .Must(v => ExamTitle.Create(v).IsSuccess)
                .WithMessage($"Title is required and must not exceed {ExamTitle.MaxLength} characters.");

            RuleFor(c => c.Description)
                .Must(v => ExamDescription.Create(v).IsSuccess)
                .WithMessage($"Description is required and must not exceed {ExamDescription.MaxLength} characters.");

            RuleFor(c => c.Subject)
                .Must(v => ExamSubject.Create(v).IsSuccess)
                .WithMessage($"Subject is required and must not exceed {ExamSubject.MaxLength} characters.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command, Guid>
    {
        public async Task<Result<Guid>> Handle(Command command, CancellationToken cancellationToken)
        {
            Result<ExamTitle> titleResult = ExamTitle.Create(command.Title);

            if (titleResult.IsFailure)
            {
                return Result.Failure<Guid>(titleResult.Error);
            }

            Result<ExamDescription> descriptionResult = ExamDescription.Create(command.Description);

            if (descriptionResult.IsFailure)
            {
                return Result.Failure<Guid>(descriptionResult.Error);
            }

            Result<ExamSubject> subjectResult = ExamSubject.Create(command.Subject);

            if (subjectResult.IsFailure)
            {
                return Result.Failure<Guid>(subjectResult.Error);
            }

            var exam = new ExamPackage
            {
                Id = Guid.NewGuid(),
                Title = titleResult.Value,
                Description = descriptionResult.Value,
                Subject = subjectResult.Value,
                OwnerProfessorId = userContext.UserId,
                Status = ExamPackageStatus.Draft,
                CreatedAt = dateTimeProvider.UtcNow
            };

            exam.Raise(new ExamPackageCreatedDomainEvent(exam.Id, exam.OwnerProfessorId));

            context.ExamPackages.Add(exam);

            await context.SaveChangesAsync(cancellationToken);

            return exam.Id;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string Title, string Description, string Subject);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("exams", async (
                Request request,
                ICommandHandler<Command, Guid> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(request.Title, request.Description, request.Subject);

                Result<Guid> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
