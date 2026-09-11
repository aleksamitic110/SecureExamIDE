using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Exams;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Exams;

public sealed class UpdateExamHandlerTests : BaseHandlerTest
{
    private static readonly Guid ProfessorId = Guid.NewGuid();

    private static UpdateExam.Handler CreateHandler(ApplicationDbContext context)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(ProfessorId);

        return new UpdateExam.Handler(context, userContext);
    }

    // Fixing one field must not require resending, or disturb, the others.
    [Fact]
    public async Task Handle_Should_ChangeOnlyTheFieldsGiven()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);

        UpdateExam.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(
            new UpdateExam.Command(exam.Id, "Compilers - final", null, null),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        ExamPackage stored = await context.ExamPackages.AsNoTracking().SingleAsync();
        stored.Title.Value.ShouldBe("Compilers - final");
        stored.Description.Value.ShouldBe("Write a parser.");
        stored.Subject.Value.ShouldBe("Compiler Construction");
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenTheExamIsPublished()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId, ExamPackageStatus.Published);

        UpdateExam.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(
            new UpdateExam.Command(exam.Id, "Renamed after publishing", null, null),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotDraft(ExamPackageStatus.Published));
        (await context.ExamPackages.AsNoTracking().SingleAsync()).Title.Value.ShouldBe("Compilers");
    }

    // Reported as missing rather than forbidden, so exam ids cannot be probed.
    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheExamBelongsToAnotherProfessor()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, Guid.NewGuid());

        UpdateExam.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(
            new UpdateExam.Command(exam.Id, "Not mine", null, null),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ExamErrors.NotFound(exam.Id));
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenTheExamIsUpdated()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        ExamPackage exam = await ExamSeed.SeedExamAsync(context, ProfessorId);

        UpdateExam.Handler handler = CreateHandler(context);

        // Act
        await handler.Handle(new UpdateExam.Command(exam.Id, null, null, "Compilers II"), CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is ExamPackageUpdatedDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_Should_RejectARequestThatChangesNothing()
    {
        // Arrange
        var validator = new UpdateExam.Validator();

        // Act
        ValidationResult result = validator.Validate(new UpdateExam.Command(Guid.NewGuid(), null, null, null));

        // Assert
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validator_Should_RejectAnEmptyTitle_WhenOneIsGiven()
    {
        // Arrange
        var validator = new UpdateExam.Validator();

        // Act
        ValidationResult result = validator.Validate(new UpdateExam.Command(Guid.NewGuid(), "   ", null, null));

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateExam.Command.Title));
    }
}
