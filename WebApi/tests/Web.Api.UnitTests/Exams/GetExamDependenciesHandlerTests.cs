using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class GetExamDependenciesHandlerTests : BaseHandlerTest
{
    private static readonly DateTime Attached = DependencySeed.CreatedAt.AddHours(1);

    private static GetExamDependencies.Query QueryFor(Guid examId) => new(examId, 1, 20);

    [Fact]
    public async Task Handle_Should_ListAPublishedExamsDependenciesInTheOrderTheyWereAttached()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await DependencySeed.SeedExamAsync(context);

        ExamDependency jdk = await DependencySeed.SeedDependencyAsync(
            context, examId, "Temurin JDK", "21.0.4", 190_000_000, Attached);
        ExamDependency gcc = await DependencySeed.SeedDependencyAsync(
            context, examId, "GCC", "14.2.0", 95_000_000, Attached.AddMinutes(5));

        var handler = new GetExamDependencies.Handler(context);

        // Act
        Result<PagedList<GetExamDependencies.Response>> result = await handler.Handle(
            QueryFor(examId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(2);
        result.Value.Items.Select(d => d.Id).ShouldBe([jdk.Id, gcc.Id]);

        GetExamDependencies.Response first = result.Value.Items[0];
        first.Name.ShouldBe("Temurin JDK");
        first.Version.ShouldBe("21.0.4");
        first.ContentType.ShouldBe("application/zip");
        first.SizeBytes.ShouldBe(190_000_000);
    }

    [Fact]
    public async Task Handle_Should_ListOnlyThatExamsDependencies()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await DependencySeed.SeedExamAsync(context);
        Guid otherExamId = await DependencySeed.SeedExamAsync(context);

        ExamDependency own = await DependencySeed.SeedDependencyAsync(
            context, examId, "GCC", "14.2.0", 1_000, Attached);
        await DependencySeed.SeedDependencyAsync(context, otherExamId, "Python", "3.13.1", 1_000, Attached);

        var handler = new GetExamDependencies.Handler(context);

        // Act
        Result<PagedList<GetExamDependencies.Response>> result = await handler.Handle(
            QueryFor(examId),
            CancellationToken.None);

        // Assert
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().Id.ShouldBe(own.Id);
    }

    // A draft is invisible to students, so its dependencies are too - reported exactly as an exam
    // that does not exist.
    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamIsStillADraft()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid examId = await DependencySeed.SeedExamAsync(context, ExamPackageStatus.Draft);
        await DependencySeed.SeedDependencyAsync(context, examId, "GCC", "14.2.0", 1_000, Attached);

        var handler = new GetExamDependencies.Handler(context);

        // Act
        Result<PagedList<GetExamDependencies.Response>> result = await handler.Handle(
            QueryFor(examId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(examId));
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamDoesNotExist()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var examId = Guid.NewGuid();

        var handler = new GetExamDependencies.Handler(context);

        // Act
        Result<PagedList<GetExamDependencies.Response>> result = await handler.Handle(
            QueryFor(examId),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(examId));
    }
}
