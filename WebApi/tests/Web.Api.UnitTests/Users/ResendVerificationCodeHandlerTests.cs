using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Users;
using Web.Api.Notifications;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Users;

public sealed class ResendVerificationCodeHandlerTests : BaseHandlerTest
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static ResendVerificationCode.Handler CreateHandler(ApplicationDbContext context, IEmailSender emailSender)
    {
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new ResendVerificationCode.Handler(
            context, emailSender, dateTimeProvider, Options.Create(new EmailVerificationOptions()));
    }

    private static ResendVerificationCode.Command Command => new(EmailVerificationSeed.Address);

    // The single row is overwritten: a fresh code, a fresh lifetime, and the failed attempts reset.
    [Fact]
    public async Task Handle_Should_ReplaceTheCodeAndMailTheNewOne()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        User user = await EmailVerificationSeed.SeedUserAsync(context);
        await EmailVerificationSeed.SeedCodeAsync(
            context, user.Id, createdAt: Now.AddMinutes(-20), expiresAt: Now.AddMinutes(-5), failedAttempts: 5);

        IEmailSender emailSender = Substitute.For<IEmailSender>();
        ResendVerificationCode.Handler handler = CreateHandler(context, emailSender);

        // Act
        Result result = await handler.Handle(Command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        EmailVerificationCode stored = await context.EmailVerificationCodes.AsNoTracking().SingleAsync();
        stored.FailedAttempts.ShouldBe(0);
        stored.ExpiresAt.ShouldBe(Now.AddMinutes(15));
        stored.CreatedAt.ShouldBe(Now);

        await emailSender.Received(1).SendAsync(
            EmailVerificationSeed.Address, EmailVerificationCodes.Subject, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_IssueACode_WhenNoneIsOutstanding()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await EmailVerificationSeed.SeedUserAsync(context);

        IEmailSender emailSender = Substitute.For<IEmailSender>();
        ResendVerificationCode.Handler handler = CreateHandler(context, emailSender);

        // Act
        await handler.Handle(Command, CancellationToken.None);

        // Assert
        (await context.EmailVerificationCodes.CountAsync()).ShouldBe(1);
        await emailSender.ReceivedWithAnyArgs(1).SendAsync(default!, default!, default!, default);
    }

    // Inside the cooldown nothing is sent and the code already on its way stays the one to use, so
    // the endpoint cannot be used to flood an inbox.
    [Fact]
    public async Task Handle_Should_SendNothing_InsideTheCooldown()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        User user = await EmailVerificationSeed.SeedUserAsync(context);
        EmailVerificationCode original = await EmailVerificationSeed.SeedCodeAsync(
            context, user.Id, createdAt: Now.AddSeconds(-10), expiresAt: Now.AddMinutes(14));
        string originalHash = original.CodeHash;

        IEmailSender emailSender = Substitute.For<IEmailSender>();
        ResendVerificationCode.Handler handler = CreateHandler(context, emailSender);

        // Act
        Result result = await handler.Handle(Command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await context.EmailVerificationCodes.AsNoTracking().SingleAsync()).CodeHash.ShouldBe(originalHash);
        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    // Answered exactly like a real resend, so the endpoint does not reveal which addresses exist.
    [Fact]
    public async Task Handle_Should_SucceedQuietly_ForAnUnknownAddress()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        IEmailSender emailSender = Substitute.For<IEmailSender>();
        ResendVerificationCode.Handler handler = CreateHandler(context, emailSender);

        // Act
        Result result = await handler.Handle(Command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_Should_SucceedQuietly_ForAnAlreadyVerifiedAccount()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await EmailVerificationSeed.SeedUserAsync(context, verified: true);

        IEmailSender emailSender = Substitute.For<IEmailSender>();
        ResendVerificationCode.Handler handler = CreateHandler(context, emailSender);

        // Act
        Result result = await handler.Handle(Command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await context.EmailVerificationCodes.CountAsync()).ShouldBe(0);
        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }
}
