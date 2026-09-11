using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Users;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Users;

public sealed class VerifyEmailHandlerTests : BaseHandlerTest
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static VerifyEmail.Handler CreateHandler(ApplicationDbContext context)
    {
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new VerifyEmail.Handler(context, dateTimeProvider, Options.Create(new EmailVerificationOptions()));
    }

    private static VerifyEmail.Command CommandWith(string code) => new(EmailVerificationSeed.Address, code);

    private static async Task<EmailVerificationCode> SeedOutstandingCodeAsync(
        ApplicationDbContext context,
        Guid userId,
        int failedAttempts = 0,
        DateTime? expiresAt = null) =>
        await EmailVerificationSeed.SeedCodeAsync(
            context, userId, Now.AddMinutes(-1), expiresAt ?? Now.AddMinutes(14), failedAttempts);

    [Fact]
    public async Task Handle_Should_VerifyTheAddressAndRetireTheCode_WhenTheCodeIsRight()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        User user = await EmailVerificationSeed.SeedUserAsync(context);
        await SeedOutstandingCodeAsync(context, user.Id);

        VerifyEmail.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(CommandWith(EmailVerificationSeed.Code), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await context.Users.AsNoTracking().SingleAsync()).EmailVerifiedAt.ShouldBe(Now);
        (await context.EmailVerificationCodes.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_CountAWrongGuess_AndLeaveTheAccountUnverified()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        User user = await EmailVerificationSeed.SeedUserAsync(context);
        await SeedOutstandingCodeAsync(context, user.Id);

        VerifyEmail.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(CommandWith("654321"), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidVerificationCode);
        (await context.EmailVerificationCodes.AsNoTracking().SingleAsync()).FailedAttempts.ShouldBe(1);
        (await context.Users.AsNoTracking().SingleAsync()).EmailVerifiedAt.ShouldBeNull();
    }

    // The cap on wrong guesses is what protects a six-digit code: once reached, even the right
    // code is refused.
    [Fact]
    public async Task Handle_Should_RefuseEvenTheRightCode_OnceTheAttemptsAreUsedUp()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        User user = await EmailVerificationSeed.SeedUserAsync(context);
        await SeedOutstandingCodeAsync(context, user.Id, failedAttempts: 5);

        VerifyEmail.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(CommandWith(EmailVerificationSeed.Code), CancellationToken.None);

        // Assert
        result.Error.ShouldBe(UserErrors.InvalidVerificationCode);
        (await context.Users.AsNoTracking().SingleAsync()).EmailVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_Should_RefuseAnExpiredCode()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        User user = await EmailVerificationSeed.SeedUserAsync(context);
        await SeedOutstandingCodeAsync(context, user.Id, expiresAt: Now.AddSeconds(-1));

        VerifyEmail.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(CommandWith(EmailVerificationSeed.Code), CancellationToken.None);

        // Assert
        result.Error.ShouldBe(UserErrors.InvalidVerificationCode);
    }

    // The same answer as a wrong code, so the endpoint does not reveal which addresses exist.
    [Fact]
    public async Task Handle_Should_AnswerAnUnknownAddressLikeAWrongCode()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        VerifyEmail.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(CommandWith(EmailVerificationSeed.Code), CancellationToken.None);

        // Assert
        result.Error.ShouldBe(UserErrors.InvalidVerificationCode);
    }

    [Fact]
    public async Task Handle_Should_RaiseDomainEvent_WhenTheAddressIsVerified()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        User user = await EmailVerificationSeed.SeedUserAsync(context);
        await SeedOutstandingCodeAsync(context, user.Id);

        VerifyEmail.Handler handler = CreateHandler(context);

        // Act
        await handler.Handle(CommandWith(EmailVerificationSeed.Code), CancellationToken.None);

        // Assert
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is UserEmailVerifiedDomainEvent)),
            Arg.Any<CancellationToken>());
    }
}
