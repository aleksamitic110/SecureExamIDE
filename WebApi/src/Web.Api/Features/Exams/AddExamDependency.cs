using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

// Phase two for dependencies: records an object the client uploaded straight to storage as a
// dependency of the exam. The key must have been minted under this exam's dependency prefix, so an
// uploaded exam file cannot be committed here instead.
//
// This is also the only place the size ceiling can be applied. A presigned URL lets the client PUT
// whatever it likes, so an oversized object is deleted here rather than recorded - which is what
// keeps the store from filling up with something no row will ever reference.
//
// The platform is the professor's statement of which computers the archive runs on - the server
// cannot tell a MinGW build from a Linux one by looking at a zip, so it is taken as given.
public static class AddExamDependency
{
    public sealed record Command(
        Guid ExamId,
        string ObjectKey,
        string Name,
        string Version,
        DependencyPlatform? Platform) : ICommand<Guid>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ObjectKey)
                .Must(v => ObjectKey.Create(v).IsSuccess)
                .WithMessage($"Object key is required and must not exceed {ObjectKey.MaxLength} characters.");

            RuleFor(c => c.Name)
                .Must(v => DependencyName.Create(v).IsSuccess)
                .WithMessage($"Name is required and must not exceed {DependencyName.MaxLength} characters.");

            RuleFor(c => c.Version)
                .Must(v => DependencyVersion.Create(v).IsSuccess)
                .WithMessage("Version is required, may contain only letters, digits, '.', '-', '_' and '+', "
                    + $"and must not exceed {DependencyVersion.MaxLength} characters.");

            // Required rather than defaulted to Any: a Windows-only compiler silently marked Any
            // would be offered to Linux students, who would find out only in the exam room.
            string platformMessage =
                $"Platform is required and must be one of: {string.Join(", ", Enum.GetNames<DependencyPlatform>())}.";

            RuleFor(c => c.Platform)
                .NotNull()
                .WithMessage(platformMessage)
                .IsInEnum()
                .WithMessage(platformMessage);
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService,
        IDateTimeProvider dateTimeProvider,
        IOptions<ExamOptions> examOptions) : ICommandHandler<Command, Guid>
    {
        public async Task<Result<Guid>> Handle(Command command, CancellationToken cancellationToken)
        {
            Result<ObjectKey> objectKeyResult = ObjectKey.Create(command.ObjectKey);

            if (objectKeyResult.IsFailure)
            {
                return Result.Failure<Guid>(objectKeyResult.Error);
            }

            Result<DependencyName> nameResult = DependencyName.Create(command.Name);

            if (nameResult.IsFailure)
            {
                return Result.Failure<Guid>(nameResult.Error);
            }

            Result<DependencyVersion> versionResult = DependencyVersion.Create(command.Version);

            if (versionResult.IsFailure)
            {
                return Result.Failure<Guid>(versionResult.Error);
            }

            if (command.Platform is not { } platform || !Enum.IsDefined(platform))
            {
                return Result.Failure<Guid>(ExamErrors.InvalidDependencyPlatform);
            }

            ObjectKey objectKey = objectKeyResult.Value;
            DependencyName name = nameResult.Value;
            DependencyVersion version = versionResult.Value;

            ExamPackageStatus? status = await context.ExamPackages
                .Where(e => e.Id == command.ExamId && e.OwnerProfessorId == userContext.UserId)
                .Select(e => (ExamPackageStatus?)e.Status)
                .SingleOrDefaultAsync(cancellationToken);

            if (status is null)
            {
                return Result.Failure<Guid>(ExamErrors.NotFound(command.ExamId));
            }

            if (status != ExamPackageStatus.Draft)
            {
                return Result.Failure<Guid>(ExamErrors.NotDraft(status.Value));
            }

            if (!ExamObjectKeys.IsDependencyOf(objectKey, command.ExamId))
            {
                return Result.Failure<Guid>(ExamErrors.ObjectKeyNotOwnedByExam);
            }

            if (await context.ExamDependencies.AnyAsync(d => d.ObjectKey == objectKey, cancellationToken))
            {
                return Result.Failure<Guid>(ExamErrors.ContentAlreadyCommitted);
            }

            // The same build twice is a conflict, and so is an Any build next to a platform-specific
            // one: a client would then be handed two copies of one tool and have no way to choose.
            bool alreadyAdded = await context.ExamDependencies.AnyAsync(
                d => d.ExamPackageId == command.ExamId
                    && d.Name == name
                    && d.Version == version
                    && (d.Platform == platform
                        || d.Platform == DependencyPlatform.Any
                        || platform == DependencyPlatform.Any),
                cancellationToken);

            if (alreadyAdded)
            {
                return Result.Failure<Guid>(ExamErrors.DependencyAlreadyAdded);
            }

            StorageObjectInfo? stored = await storageService.StatAsync(objectKey.Value, cancellationToken);

            if (stored is null)
            {
                return Result.Failure<Guid>(ExamErrors.ContentNotUploaded);
            }

            long maxBytes = examOptions.Value.ResolvedMaxDependencyBytes;

            if (stored.SizeBytes > maxBytes)
            {
                // Nothing references it and nothing ever will, so it is removed now rather than
                // left as an orphan the project has no sweeper for.
                await storageService.DeleteAsync(objectKey.Value, cancellationToken);

                return Result.Failure<Guid>(ExamErrors.DependencyTooLarge(stored.SizeBytes, maxBytes));
            }

            Result<ContentType> contentTypeResult = ContentType.Create(stored.ContentType);
            ContentType contentType = contentTypeResult.IsSuccess
                ? contentTypeResult.Value
                : ContentType.Default;

            var dependency = new ExamDependency
            {
                Id = Guid.NewGuid(),
                ExamPackageId = command.ExamId,
                Name = name,
                Version = version,
                Platform = platform,
                ContentType = contentType,
                ObjectKey = objectKey,

                // Taken from the store, not from the caller, so the row cannot misdescribe the object.
                SizeBytes = stored.SizeBytes,
                CreatedAt = dateTimeProvider.UtcNow
            };

            dependency.Raise(new ExamDependencyAddedDomainEvent(dependency.Id, dependency.ExamPackageId));

            context.ExamDependencies.Add(dependency);

            await context.SaveChangesAsync(cancellationToken);

            return dependency.Id;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string ObjectKey, string Name, string Version, DependencyPlatform? Platform);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("exams/{examId:guid}/dependencies", async (
                Guid examId,
                Request request,
                ICommandHandler<Command, Guid> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(
                    examId,
                    request.ObjectKey,
                    request.Name,
                    request.Version,
                    request.Platform);

                Result<Guid> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
