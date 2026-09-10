using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Crypto;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.Storage;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;
using Web.Api.Features.Exams;

namespace Web.Api.Features.ExamSessions;

// Scheduling a sitting is what seals the exam. The files are gathered into an archive, encrypted
// under a fresh content key, and that key is wrapped with one derived from a one-time code the
// professor is shown exactly once. From here the server holds only ciphertext and a digest, so
// the package can sit on a student's laptop for weeks and still be unopenable until exam day.
public static class CreateExamSession
{
    public sealed record Command(Guid ExamId, DateTime StartsAt, DateTime EndsAt) : ICommand<Response>;

    // OneTimeCode appears here and nowhere else, ever. Like the device credential, it is the one
    // moment the plaintext exists outside the professor's hands.
    public sealed record Response(
        Guid SessionId,
        string OneTimeCode,
        DateTime StartsAt,
        DateTime EndsAt,
        long PackageSizeBytes,
        string PackageSha256);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ExamId).NotEmpty();
            RuleFor(c => c.StartsAt).NotEmpty();
            RuleFor(c => c.EndsAt).NotEmpty();
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IStorageService storageService,
        ICryptoService cryptoService,
        IOneTimeCodeProvider oneTimeCodeProvider,
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (command.EndsAt <= command.StartsAt)
            {
                return Result.Failure<Response>(ExamSessionErrors.EndsBeforeItStarts);
            }

            if (command.EndsAt <= dateTimeProvider.UtcNow)
            {
                return Result.Failure<Response>(ExamSessionErrors.AlreadyOver);
            }

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

            if (status != ExamPackageStatus.Published)
            {
                return Result.Failure<Response>(ExamSessionErrors.ExamNotPublished);
            }

            List<ExamFileToSeal> files = await context.ExamFiles
                .AsNoTracking()
                .Where(f => f.ExamPackageId == command.ExamId)
                .OrderBy(f => f.CreatedAt)
                .Select(f => new ExamFileToSeal(f.FileName.Value, f.ObjectKey.Value))
                .ToListAsync(cancellationToken);

            if (files.Count == 0)
            {
                return Result.Failure<Response>(ExamSessionErrors.NoFiles);
            }

            var sessionId = Guid.NewGuid();
            string oneTimeCode = oneTimeCodeProvider.Generate();

            byte[] archive = await BuildArchiveAsync(files, cancellationToken);
            SealedPackage sealedPackage = cryptoService.Seal(archive, oneTimeCode);

            // The plaintext archive has done its job; leaving a copy of every exam task on the heap
            // for the GC to collect at its leisure would be careless.
            CryptographicOperations.ZeroMemory(archive);

            ObjectKey packageKey = ExamObjectKeys.PackageKeyFor(command.ExamId, sessionId);
            ObjectKey headerKey = ExamObjectKeys.HeaderKeyFor(command.ExamId, sessionId);

            // Storage first, database second - the same ordering every upload in this project uses.
            // A row naming a package that was never written would only be discovered by a student
            // with no network on exam day.
            await PutAsync(packageKey, sealedPackage.Ciphertext, "application/octet-stream", cancellationToken);

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(sealedPackage.Header, HeaderJsonOptions);
            await PutAsync(headerKey, headerBytes, "application/json", cancellationToken);

            var session = new ExamSession
            {
                Id = sessionId,
                ExamPackageId = command.ExamId,
                CreatedByProfessorId = userContext.UserId,
                StartsAt = command.StartsAt,
                EndsAt = command.EndsAt,
                IsActive = true,
                OneTimeCodeHash = Sha256Hash.Create(oneTimeCodeProvider.Hash(oneTimeCode)).Value,
                PackageObjectKey = packageKey,
                HeaderObjectKey = headerKey,
                PackageSizeBytes = sealedPackage.Ciphertext.LongLength,
                PackageSha256 = Sha256Hash.Create(
                    Convert.ToHexStringLower(SHA256.HashData(sealedPackage.Ciphertext))).Value,
                CreatedAt = dateTimeProvider.UtcNow
            };

            session.Raise(new ExamSessionCreatedDomainEvent(session.Id, session.ExamPackageId));

            context.ExamSessions.Add(session);

            await context.SaveChangesAsync(cancellationToken);

            return new Response(
                session.Id,
                oneTimeCode,
                session.StartsAt,
                session.EndsAt,
                session.PackageSizeBytes,
                session.PackageSha256.Value);
        }

        // The archive keeps the original file names, so the client can lay the exam out on disk
        // exactly as the professor uploaded it. It is built in memory on purpose: exam tasks are
        // documents, and AesGcm has no streaming form. Dependencies - the large content - are
        // never sealed, so nothing here scales with a toolchain download.
        private async Task<byte[]> BuildArchiveAsync(
            IReadOnlyCollection<ExamFileToSeal> files,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (ExamFileToSeal file in files)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(file.FileName, CompressionLevel.Optimal);

                    await using Stream source = await storageService.GetAsync(file.ObjectKey, cancellationToken);
                    await using Stream target = await entry.OpenAsync(cancellationToken);

                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            return buffer.ToArray();
        }

        private async Task PutAsync(
            ObjectKey objectKey,
            byte[] content,
            string contentType,
            CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream(content, writable: false);

            await storageService.PutAsync(objectKey.Value, stream, contentType, cancellationToken);
        }

        private sealed record ExamFileToSeal(string FileName, string ObjectKey);

        private static readonly JsonSerializerOptions HeaderJsonOptions = new(JsonSerializerDefaults.Web);
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(DateTime StartsAt, DateTime EndsAt);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("exams/{examId:guid}/sessions", async (
                Guid examId,
                Request request,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(examId, request.StartsAt, request.EndsAt);

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsManage)
            .WithTags(Tags.ExamSessions);
        }
    }
}
