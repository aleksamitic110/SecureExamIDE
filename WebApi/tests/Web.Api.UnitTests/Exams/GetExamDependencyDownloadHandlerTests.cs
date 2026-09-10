using Web.Api.Common;
using Web.Api.Common.Storage;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class GetExamDependencyDownloadHandlerTests : BaseHandlerTest
{
    private static readonly DateTime Now = DependencySeed.CreatedAt.AddDays(3);

    private static GetExamDependencyDownload.Handler CreateHandler(
        ApplicationDbContext context,
        IStorageService storageService)
    {
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new GetExamDependencyDownload.Handler(context, storageService, dateTimeProvider);
    }

    // A storage double that signs a recognisable URL for whatever key it is given.
    private static IStorageService SigningStorage()
    {
        IStorageService storageService = Substitute.For<IStorageService>();
        storageService
            .CreatePresignedDownloadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => $"https://storage.test/{callInfo.Arg<string>()}");

        return storageService;
    }

    [Fact]
    public async Task Handle_Should_ReturnALinkToTheDependencyValidForTwoHours()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await DependencySeed.SeedExamAsync(context);
        ExamDependency jdk = await DependencySeed.SeedDependencyAsync(
            context, examId, "Temurin JDK", "21.0.4", 190_000_000, DependencySeed.CreatedAt);

        IStorageService storageService = SigningStorage();
        GetExamDependencyDownload.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<GetExamDependencyDownload.Response> result = await handler.Handle(
            new GetExamDependencyDownload.Query(jdk.Id),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        GetExamDependencyDownload.Response download = result.Value;
        download.DownloadUrl.ShouldBe($"https://storage.test/{jdk.ObjectKey.Value}");
        download.ExpiresAt.ShouldBe(Now.AddHours(2));
        download.Name.ShouldBe("Temurin JDK");
        download.Version.ShouldBe("21.0.4");
        download.SizeBytes.ShouldBe(190_000_000);

        await storageService.Received(1).CreatePresignedDownloadUrlAsync(
            jdk.ObjectKey.Value, TimeSpan.FromHours(2), Arg.Any<CancellationToken>());
    }

    // Nothing leaks out of an exam before it is published: no URL is even signed.
    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamIsStillADraft()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await DependencySeed.SeedExamAsync(context, ExamPackageStatus.Draft);
        ExamDependency gcc = await DependencySeed.SeedDependencyAsync(
            context, examId, "GCC", "14.2.0", 1_000, DependencySeed.CreatedAt);

        IStorageService storageService = SigningStorage();
        GetExamDependencyDownload.Handler handler = CreateHandler(context, storageService);

        // Act
        Result<GetExamDependencyDownload.Response> result = await handler.Handle(
            new GetExamDependencyDownload.Query(gcc.Id),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.DependencyNotFound(gcc.Id));

        await storageService.DidNotReceiveWithAnyArgs()
            .CreatePresignedDownloadUrlAsync(default!, default, default);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheDependencyDoesNotExist()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var dependencyId = Guid.NewGuid();

        GetExamDependencyDownload.Handler handler = CreateHandler(context, SigningStorage());

        // Act
        Result<GetExamDependencyDownload.Response> result = await handler.Handle(
            new GetExamDependencyDownload.Query(dependencyId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.DependencyNotFound(dependencyId));
    }
}
