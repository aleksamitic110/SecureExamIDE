using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Devices;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Devices;

public sealed class RevokeDeviceHandlerTests : BaseHandlerTest
{
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly DateTime RevokedAt = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    private static RevokeDevice.Handler CreateHandler(ApplicationDbContext context)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(CallerId);

        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(RevokedAt);

        return new RevokeDevice.Handler(context, userContext, dateTimeProvider);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenDeviceDoesNotExist()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        RevokeDevice.Handler handler = CreateHandler(context);

        var deviceId = Guid.NewGuid();

        // Act
        Result result = await handler.Handle(new RevokeDevice.Command(deviceId), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DeviceErrors.NotFound(deviceId));
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenDeviceBelongsToAnotherUser()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid deviceId = await SeedDeviceAsync(context, ownerId: Guid.NewGuid());

        RevokeDevice.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new RevokeDevice.Command(deviceId), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DeviceErrors.NotFound(deviceId));

        DeviceCredential device = await context.DeviceCredentials.SingleAsync();
        device.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenDeviceIsAlreadyRevoked()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid deviceId = await SeedDeviceAsync(context, ownerId: CallerId, revokedAt: DateTime.UtcNow);

        RevokeDevice.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new RevokeDevice.Command(deviceId), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DeviceErrors.AlreadyRevoked);
    }

    [Fact]
    public async Task Handle_Should_StampRevokedAtAndRaiseDomainEvent_WhenDeviceIsOwned()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);
        Guid deviceId = await SeedDeviceAsync(context, ownerId: CallerId);

        RevokeDevice.Handler handler = CreateHandler(context);

        // Act
        Result result = await handler.Handle(new RevokeDevice.Command(deviceId), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        DeviceCredential device = await context.DeviceCredentials.SingleAsync();
        device.RevokedAt.ShouldBe(RevokedAt);

        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events =>
                events.Any(e => e is DeviceCredentialRevokedDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    private static async Task<Guid> SeedDeviceAsync(
        ApplicationDbContext context,
        Guid ownerId,
        DateTime? revokedAt = null)
    {
        var device = new DeviceCredential
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            DeviceName = "Laptop".AsDeviceName(),
            SecretHash = $"hash-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            RevokedAt = revokedAt
        };

        context.DeviceCredentials.Add(device);
        await context.SaveChangesAsync();

        return device.Id;
    }
}
