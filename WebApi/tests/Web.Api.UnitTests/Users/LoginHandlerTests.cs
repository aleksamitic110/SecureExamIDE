using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Devices;
using Web.Api.Features.Users;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Users;

public sealed class LoginHandlerTests : BaseHandlerTest
{
    private const string EmailAddress = "test@example.com";
    private const string Password = "Password123";
    private const string DeviceName = "Second laptop";

    private static Login.Command Command => new(EmailAddress, Password, DeviceName);

    [Fact]
    public async Task Handle_Should_ReturnFailure_WhenUserDoesNotExist()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var handler = new Login.Handler(
            context,
            Substitute.For<IPasswordHasher>(),
            Substitute.For<ITokenProvider>(),
            Substitute.For<IDeviceCredentialProvider>(),
            Substitute.For<IDateTimeProvider>());

        // Act
        Result<Login.Response> result = await handler.Handle(Command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.NotFoundByEmail);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_WhenPasswordIsInvalid()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await SeedUserAsync(context);

        IPasswordHasher passwordHasher = Substitute.For<IPasswordHasher>();
        passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var handler = new Login.Handler(
            context,
            passwordHasher,
            Substitute.For<ITokenProvider>(),
            Substitute.For<IDeviceCredentialProvider>(),
            Substitute.For<IDateTimeProvider>());

        // Act
        Result<Login.Response> result = await handler.Handle(Command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.NotFoundByEmail);
    }

    [Fact]
    public async Task Handle_Should_NotIssueDeviceCredential_WhenPasswordIsInvalid()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await SeedUserAsync(context);

        IPasswordHasher passwordHasher = Substitute.For<IPasswordHasher>();
        passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var handler = new Login.Handler(
            context,
            passwordHasher,
            Substitute.For<ITokenProvider>(),
            Substitute.For<IDeviceCredentialProvider>(),
            Substitute.For<IDateTimeProvider>());

        // Act
        await handler.Handle(Command, CancellationToken.None);

        // Assert
        (await context.DeviceCredentials.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_BindNewDeviceAndPersistRefreshToken_WhenCredentialsAreValid()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid userId = await SeedUserAsync(context);

        IPasswordHasher passwordHasher = Substitute.For<IPasswordHasher>();
        passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        ITokenProvider tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.Create(Arg.Any<User>()).Returns("access-token");
        tokenProvider.GenerateRefreshToken().Returns("refresh-token");

        IDeviceCredentialProvider deviceCredentialProvider = Substitute.For<IDeviceCredentialProvider>();
        deviceCredentialProvider.Generate().Returns("device-secret");
        deviceCredentialProvider.Hash("device-secret").Returns("device-secret-hash");

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(DateTime.UtcNow);

        var handler = new Login.Handler(
            context,
            passwordHasher,
            tokenProvider,
            deviceCredentialProvider,
            dateTimeProvider);

        // Act
        Result<Login.Response> result = await handler.Handle(Command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access-token");
        result.Value.RefreshToken.ShouldBe("refresh-token");
        result.Value.DeviceCredential.ShouldBe("device-secret");

        RefreshToken refreshToken = await context.RefreshTokens.SingleAsync();
        refreshToken.Token.ShouldBe("refresh-token");
        refreshToken.ExpiresOnUtc.ShouldBeGreaterThan(dateTimeProvider.UtcNow);

        DeviceCredential device = await context.DeviceCredentials.SingleAsync();
        device.Id.ShouldBe(result.Value.DeviceId);
        device.UserId.ShouldBe(userId);
        device.DeviceName!.Value.ShouldBe(DeviceName);
        device.SecretHash.ShouldBe("device-secret-hash");
        device.RevokedAt.ShouldBeNull();
    }

    // Checked after the password, so only someone who already knows it learns that the address is
    // not verified - and nothing is issued.
    [Fact]
    public async Task Handle_Should_RefuseAnUnverifiedAccount_WhenThePasswordIsRight()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await SeedUserAsync(context, emailVerified: false);

        IPasswordHasher passwordHasher = Substitute.For<IPasswordHasher>();
        passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var handler = new Login.Handler(
            context,
            passwordHasher,
            Substitute.For<ITokenProvider>(),
            Substitute.For<IDeviceCredentialProvider>(),
            Substitute.For<IDateTimeProvider>());

        // Act
        Result<Login.Response> result = await handler.Handle(Command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.EmailNotVerified);
        (await context.DeviceCredentials.CountAsync()).ShouldBe(0);
        (await context.RefreshTokens.CountAsync()).ShouldBe(0);
    }

    private static async Task<Guid> SeedUserAsync(ApplicationDbContext context, bool emailVerified = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = EmailAddress.AsEmail(),
            FirstName = "Test".AsPersonName(),
            LastName = "User".AsPersonName(),
            PasswordHash = "hash",
            Role = Role.Student,
            IndexNumber = "19252".AsIndexNumber(),
            IsActive = true,
            EmailVerifiedAt = emailVerified ? DateTime.UtcNow : null
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }
}
