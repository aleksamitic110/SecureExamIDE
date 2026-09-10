using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Devices;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Devices;

public sealed class GetDevicesHandlerTests : BaseHandlerTest
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private static GetDevices.Handler CreateHandler(ApplicationDbContext context)
    {
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(CallerId);

        return new GetDevices.Handler(context, userContext);
    }

    [Fact]
    public async Task Handle_Should_ReturnOnlyTheCallersDevices()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid ownDevice = await SeedDeviceAsync(context, CallerId, "My laptop", DateTime.UtcNow);
        await SeedDeviceAsync(context, Guid.NewGuid(), "Someone else's laptop", DateTime.UtcNow);

        GetDevices.Handler handler = CreateHandler(context);

        // Act
        Result<IReadOnlyCollection<GetDevices.Response>> result =
            await handler.Handle(new GetDevices.Query(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value.Single().Id.ShouldBe(ownDevice);
        result.Value.Single().DeviceName.ShouldBe("My laptop");
    }

    [Fact]
    public async Task Handle_Should_ReturnNewestFirst()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var now = DateTime.UtcNow;
        await SeedDeviceAsync(context, CallerId, "Older", now.AddDays(-2));
        await SeedDeviceAsync(context, CallerId, "Newer", now);

        GetDevices.Handler handler = CreateHandler(context);

        // Act
        Result<IReadOnlyCollection<GetDevices.Response>> result =
            await handler.Handle(new GetDevices.Query(), CancellationToken.None);

        // Assert
        result.Value.Select(d => d.DeviceName).ShouldBe(["Newer", "Older"]);
    }

    [Fact]
    public async Task Handle_Should_ListRevokedDevicesWithTheirRevocationTime()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        DateTime revokedAt = DateTime.UtcNow;
        await SeedDeviceAsync(context, CallerId, "Lost laptop", DateTime.UtcNow, revokedAt);

        GetDevices.Handler handler = CreateHandler(context);

        // Act
        Result<IReadOnlyCollection<GetDevices.Response>> result =
            await handler.Handle(new GetDevices.Query(), CancellationToken.None);

        // Assert
        result.Value.Single().RevokedAt.ShouldBe(revokedAt);
    }

    private static async Task<Guid> SeedDeviceAsync(
        ApplicationDbContext context,
        Guid ownerId,
        string deviceName,
        DateTime createdAt,
        DateTime? revokedAt = null)
    {
        var device = new DeviceCredential
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            DeviceName = deviceName.AsDeviceName(),
            SecretHash = $"hash-{Guid.NewGuid():N}",
            CreatedAt = createdAt,
            RevokedAt = revokedAt
        };

        context.DeviceCredentials.Add(device);
        await context.SaveChangesAsync();

        return device.Id;
    }
}
