using System.Text;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class UploadExamFileContentHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();

    private static UploadExamFileContent.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService? storageService = null)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new UploadExamFileContent.Handler(
            context,
            userContext,
            storageService ?? Substitute.For<IStorageService>());
    }

    private static MemoryStream ContentOf(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ownerId: Guid.NewGuid());

        IStorageService storageService = Substitute.For<IStorageService>();
        UploadExamFileContent.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<UploadExamFileContent.Response> result = await handler.Handle(
            new UploadExamFileContent.Command(examId, ContentOf("task"), "text/plain"),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(examId));

        await storageService.DidNotReceiveWithAnyArgs()
            .PutAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenExamIsNoLongerADraft()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);

        UploadExamFileContent.Handler handler = CreateHandler(context);

        // Act
        Result<UploadExamFileContent.Response> result = await handler.Handle(
            new UploadExamFileContent.Command(examId, ContentOf("task"), "text/plain"),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotDraft(ExamPackageStatus.Published));
    }

    [Fact]
    public async Task Handle_Should_ReturnProblem_WhenContentIsEmpty()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);

        UploadExamFileContent.Handler handler = CreateHandler(context);

        // Act
        Result<UploadExamFileContent.Response> result = await handler.Handle(
            new UploadExamFileContent.Command(examId, new MemoryStream(), "text/plain"),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.EmptyContent);
    }

    [Fact]
    public async Task Handle_Should_StoreContentUnderTheExamsOwnPrefix()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);

        IStorageService storageService = Substitute.For<IStorageService>();
        UploadExamFileContent.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<UploadExamFileContent.Response> result = await handler.Handle(
            new UploadExamFileContent.Command(examId, ContentOf("task"), "text/plain"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ObjectKey.ShouldStartWith($"exams/{examId}/files/");

        // The digest goes into storage with the bytes, which is what AddExamFile reads back.
        await storageService.Received(1).PutAsync(
            result.Value.ObjectKey,
            Arg.Any<Stream>(),
            "text/plain",
            result.Value.Sha256,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReportTheServersOwnHashAndSize()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);

        UploadExamFileContent.Handler handler = CreateHandler(context);

        // Act
        Result<UploadExamFileContent.Response> result = await handler.Handle(
            new UploadExamFileContent.Command(examId, ContentOf("abc"), "text/plain"),
            CancellationToken.None);

        // Assert - the published SHA-256 of "abc".
        result.Value.Sha256.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        result.Value.SizeBytes.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_Should_WriteNoDatabaseRow()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await SeedExamAsync(context, ProfessorId);

        UploadExamFileContent.Handler handler = CreateHandler(context);

        // Act
        await handler.Handle(
            new UploadExamFileContent.Command(examId, ContentOf("task"), "text/plain"),
            CancellationToken.None);

        // Assert - phase one stores bytes only; the row is AddExamFile's job.
        context.ExamFiles.Count().ShouldBe(0);
    }

    private static async Task<Guid> SeedExamAsync(
        ApplicationDbContext context,
        Guid ownerId,
        ExamPackageStatus status = ExamPackageStatus.Draft)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Algorithms".AsExamTitle(),
            Description = "Final exam".AsExamDescription(),
            Subject = "Algorithms and Data Structures".AsExamSubject(),
            OwnerProfessorId = ownerId,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

        context.ExamPackages.Add(exam);
        await context.SaveChangesAsync();

        return exam.Id;
    }
}
