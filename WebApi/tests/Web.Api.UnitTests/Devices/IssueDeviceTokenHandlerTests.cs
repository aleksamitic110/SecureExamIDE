using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Devices;
using Web.Api.Features.Users;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Devices;

public sealed class IssueDeviceTokenHandlerTests : BaseHandlerTest
{
    private const string Secret = "device-secret";
    private const string SecretHash = "device-secret-hash";

    private static IDeviceCredentialProvider CreateCredentialProvider()
    {
        IDeviceCredentialProvider provider = Substitute.For<IDeviceCredentialProvider>();
        provider.Hash(Secret).Returns(SecretHash);

        return provider;
    }

    [Fact]
    public async Task Handle_Should_ReturnInvalidCredential_WhenSecretIsUnknown()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        var handler = new IssueDeviceToken.Handler(
            context,
            CreateCredentialProvider(),
            Substitute.For<ITokenProvider>());

        // Act
        Result<IssueDeviceToken.Response> result = await handler.Handle(
            new IssueDeviceToken.Command(Secret),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DeviceErrors.InvalidCredential);
    }

    [Fact]
    public async Task Handle_Should_ReturnInvalidCredential_WhenDeviceIsRevoked()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await SeedAsync(context, isActive: true, revokedAt: DateTime.UtcNow);

        var handler = new IssueDeviceToken.Handler(
            context,
            CreateCredentialProvider(),
            Substitute.For<ITokenProvider>());

        // Act
        Result<IssueDeviceToken.Response> result = await handler.Handle(
            new IssueDeviceToken.Command(Secret),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DeviceErrors.InvalidCredential);
    }

    [Fact]
    public async Task Handle_Should_ReturnInvalidCredential_WhenOwnerIsDeactivated()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await SeedAsync(context, isActive: false, revokedAt: null);

        var handler = new IssueDeviceToken.Handler(
            context,
            CreateCredentialProvider(),
            Substitute.For<ITokenProvider>());

        // Act
        Result<IssueDeviceToken.Response> result = await handler.Handle(
            new IssueDeviceToken.Command(Secret),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DeviceErrors.InvalidCredential);
    }

    [Fact]
    public async Task Handle_Should_MintTokenForTheOwningDevice_WhenCredentialIsValid()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        (Guid userId, Guid deviceId) = await SeedAsync(context, isActive: true, revokedAt: null);

        ITokenProvider tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.CreateForDevice(Arg.Any<User>(), deviceId).Returns("device-token");

        var handler = new IssueDeviceToken.Handler(context, CreateCredentialProvider(), tokenProvider);

        // Act
        Result<IssueDeviceToken.Response> result = await handler.Handle(
            new IssueDeviceToken.Command(Secret),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("device-token");
        tokenProvider.Received(1).CreateForDevice(Arg.Is<User>(u => u.Id == userId), deviceId);
    }

    // Registration hands the credential out before the address is confirmed, so a valid, unrevoked
    // credential of an unverified account still gets no token - and is told why.
    [Fact]
    public async Task Handle_Should_RefuseTheCredentialOfAnUnverifiedAccount()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        await SeedAsync(context, isActive: true, revokedAt: null, emailVerified: false);

        ITokenProvider tokenProvider = Substitute.For<ITokenProvider>();
        var handler = new IssueDeviceToken.Handler(context, CreateCredentialProvider(), tokenProvider);

        // Act
        Result<IssueDeviceToken.Response> result = await handler.Handle(
            new IssueDeviceToken.Command(Secret),
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.EmailNotVerified);
        tokenProvider.DidNotReceiveWithAnyArgs().CreateForDevice(default!, Guid.Empty);
    }

    private static async Task<(Guid UserId, Guid DeviceId)> SeedAsync(
        ApplicationDbContext context,
        bool isActive,
        DateTime? revokedAt,
        bool emailVerified = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "student@example.com".AsEmail(),
            FirstName = "Test".AsPersonName(),
            LastName = "Student".AsPersonName(),
            PasswordHash = "hash",
            Role = Role.Student,
            IndexNumber = "19252".AsIndexNumber(),
            IsActive = isActive,
            EmailVerifiedAt = emailVerified ? DateTime.UtcNow : null
        };

        var device = new DeviceCredential
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceName = "Laptop".AsDeviceName(),
            SecretHash = SecretHash,
            CreatedAt = DateTime.UtcNow,
            RevokedAt = revokedAt
        };

        context.Users.Add(user);
        context.DeviceCredentials.Add(device);
        await context.SaveChangesAsync();

        return (user.Id, device.Id);
    }
}
