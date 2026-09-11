using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Users;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Users;

public sealed class GetUserByEmailHandlerTests : BaseHandlerTest
{
    private static async Task<User> SeedAsync(
        ApplicationDbContext context,
        string email,
        Role role,
        string? indexNumber,
        bool verified)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.AsEmail(),
            FirstName = "Marko".AsPersonName(),
            LastName = "Markovic".AsPersonName(),
            PasswordHash = "hash",
            Role = role,
            IndexNumber = indexNumber?.AsIndexNumber(),
            IsActive = true,
            EmailVerifiedAt = verified ? DateTime.UtcNow : null
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user;
    }

    [Fact]
    public async Task Handle_Should_ReturnTheStudentWithIndexNumberAndVerificationState()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        User student = await SeedAsync(context, "marko@example.com", Role.Student, "19252", verified: false);

        var handler = new GetUserByEmail.Handler(context);

        // Act
        Result<GetUserByEmail.Response> result = await handler.Handle(
            new GetUserByEmail.Query("marko@example.com"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(student.Id);
        result.Value.FirstName.ShouldBe("Marko");
        result.Value.LastName.ShouldBe("Markovic");
        result.Value.Role.ShouldBe("Student");
        result.Value.IndexNumber.ShouldBe("19252");
        result.Value.IsEmailVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_Should_ReturnAProfessorWithoutAnIndexNumber()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await SeedAsync(context, "professor@example.com", Role.Professor, indexNumber: null, verified: true);

        var handler = new GetUserByEmail.Handler(context);

        // Act
        Result<GetUserByEmail.Response> result = await handler.Handle(
            new GetUserByEmail.Query("professor@example.com"),
            CancellationToken.None);

        // Assert
        result.Value.Role.ShouldBe("Professor");
        result.Value.IndexNumber.ShouldBeNull();
        result.Value.IsEmailVerified.ShouldBeTrue();
    }

    // Addresses are normalised on construction, so the casing the professor types does not matter.
    [Fact]
    public async Task Handle_Should_FindTheAccountRegardlessOfCase()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        User student = await SeedAsync(context, "marko@example.com", Role.Student, "19252", verified: true);

        var handler = new GetUserByEmail.Handler(context);

        // Act
        Result<GetUserByEmail.Response> result = await handler.Handle(
            new GetUserByEmail.Query("  Marko@Example.COM "),
            CancellationToken.None);

        // Assert
        result.Value.Id.ShouldBe(student.Id);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenNoAccountHasTheAddress()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var handler = new GetUserByEmail.Handler(context);

        // Act
        Result<GetUserByEmail.Response> result = await handler.Handle(
            new GetUserByEmail.Query("nobody@example.com"),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.NotFoundByEmail);
    }

    [Fact]
    public async Task Handle_Should_ReturnProblem_WhenTheAddressIsMalformed()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var handler = new GetUserByEmail.Handler(context);

        // Act
        Result<GetUserByEmail.Response> result = await handler.Handle(
            new GetUserByEmail.Query("not-an-address"),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Problem);
    }
}
