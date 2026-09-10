using FluentValidation;
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

// Phase two of the two-phase upload: records an already-stored object as a file of the exam. The
// key is proved to belong to this exam and to exist in storage before any row is written, and the
// size, content type and SHA-256 are all read back from the store rather than taken from the
// request. The digest in particular was measured by UploadExamFileContent and stored with the
// object, so the caller has no say in what gets recorded.
public static class AddExamFile
{
    public sealed record Command(Guid ExamId, string ObjectKey, string FileName) : ICommand<Guid>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ObjectKey)
                .Must(v => ObjectKey.Create(v).IsSuccess)
                .WithMessage($"Object key is required and must not exceed {ObjectKey.MaxLength} characters.");

            RuleFor(c => c.FileName)
                .Must(v => FileName.Create(v).IsSuccess)
                .WithMessage("File name is required, must not contain a path separator, and must not exceed "
                    + $"{FileName.MaxLength} characters.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService,
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command, Guid>
    {
        public async Task<Result<Guid>> Handle(Command command, CancellationToken cancellationToken)
        {
            Result<ObjectKey> objectKeyResult = ObjectKey.Create(command.ObjectKey);

            if (objectKeyResult.IsFailure)
            {
                return Result.Failure<Guid>(objectKeyResult.Error);
            }

            Result<FileName> fileNameResult = FileName.Create(command.FileName);

            if (fileNameResult.IsFailure)
            {
                return Result.Failure<Guid>(fileNameResult.Error);
            }

            ObjectKey objectKey = objectKeyResult.Value;

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

            // Without this the owner of one exam could attach another exam's stored object to it.
            if (!ExamObjectKeys.IsFileOf(objectKey, command.ExamId))
            {
                return Result.Failure<Guid>(ExamErrors.ObjectKeyNotOwnedByExam);
            }

            if (await context.ExamFiles.AnyAsync(f => f.ObjectKey == objectKey, cancellationToken))
            {
                return Result.Failure<Guid>(ExamErrors.ContentAlreadyCommitted);
            }

            // The check that gives the split its point: no row is written unless the bytes are
            // demonstrably already in storage.
            StorageObjectInfo? stored = await storageService.StatAsync(objectKey.Value, cancellationToken);

            // An object with no recorded digest was not written by phase one, which always records
            // one - so as far as this commit is concerned, nothing was uploaded under that key.
            Result<Sha256Hash>? sha256Result = stored?.Sha256 is null ? null : Sha256Hash.Create(stored.Sha256);

            if (stored is null || sha256Result is null || sha256Result.IsFailure)
            {
                return Result.Failure<Guid>(ExamErrors.ContentNotUploaded);
            }

            Result<ContentType> contentTypeResult = ContentType.Create(stored.ContentType);
            ContentType contentType = contentTypeResult.IsSuccess
                ? contentTypeResult.Value
                : ContentType.Default;

            var file = new ExamFile
            {
                Id = Guid.NewGuid(),
                ExamPackageId = command.ExamId,
                FileName = fileNameResult.Value,
                ContentType = contentType,
                ObjectKey = objectKey,

                // Taken from the store, not from the caller, so the row cannot misdescribe the object.
                SizeBytes = stored.SizeBytes,
                Sha256 = sha256Result.Value,
                CreatedAt = dateTimeProvider.UtcNow
            };

            file.Raise(new ExamFileAddedDomainEvent(file.Id, file.ExamPackageId));

            context.ExamFiles.Add(file);

            await context.SaveChangesAsync(cancellationToken);

            return file.Id;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string ObjectKey, string FileName);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("exams/{examId:guid}/files", async (
                Guid examId,
                Request request,
                ICommandHandler<Command, Guid> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(examId, request.ObjectKey, request.FileName);

                Result<Guid> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.Exams);
        }
    }
}
