using Web.Api.Authorization;
using Web.Api.Database;
using Web.Api.Features.Devices;
using Web.Api.Features.Users;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Authorization;

public sealed class PermissionProviderTests : BaseHandlerTest
{
    [Fact]
    public void GetForRole_Should_GrantSubmissionsSubmitButNotExamsManage_ForStudent()
    {
        // Act
        HashSet<string> permissions = PermissionProvider.GetForRole(Role.Student);

        // Assert
        permissions.ShouldContain(Permissions.UsersRead);
        permissions.ShouldContain(Permissions.ExamsRead);
        permissions.ShouldContain(Permissions.SubmissionsSubmit);
        permissions.ShouldNotContain(Permissions.ExamsManage);
        permissions.ShouldNotContain(Permissions.SubmissionsReview);
        permissions.ShouldNotContain(Permissions.UsersLookup);
    }

    [Fact]
    public void GetForRole_Should_GrantExamsManageButNotSubmissionsSubmit_ForProfessor()
    {
        // Act
        HashSet<string> permissions = PermissionProvider.GetForRole(Role.Professor);

        // Assert
        permissions.ShouldContain(Permissions.UsersRead);
        permissions.ShouldContain(Permissions.ExamsRead);
        permissions.ShouldContain(Permissions.ExamsManage);
        permissions.ShouldContain(Permissions.SubmissionsReview);
        permissions.ShouldContain(Permissions.UsersLookup);
        permissions.ShouldNotContain(Permissions.SubmissionsSubmit);
    }

    [Fact]
    public async Task GetForUserIdAsync_Should_ReturnRolePermissions_WhenUserIsActive()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid userId = await AddUserAsync(context, Role.Professor, isActive: true);

        var provider = new PermissionProvider(context);

        // Act
        HashSet<string> permissions = await provider.GetForUserIdAsync(userId);

        // Assert
        permissions.ShouldBe(PermissionProvider.GetForRole(Role.Professor), ignoreOrder: true);
    }

    [Fact]
    public async Task GetForUserIdAsync_Should_ReturnNothing_WhenUserIsDeactivated()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid userId = await AddUserAsync(context, Role.Student, isActive: false);

        var provider = new PermissionProvider(context);

        // Act
        HashSet<string> permissions = await provider.GetForUserIdAsync(userId);

        // Assert
        permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetForUserIdAsync_Should_ReturnNothing_WhenUserDoesNotExist()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        var provider = new PermissionProvider(context);

        // Act
        HashSet<string> permissions = await provider.GetForUserIdAsync(Guid.NewGuid());

        // Assert
        permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetForUserIdAsync_Should_ReturnRolePermissions_WhenTheTokensDeviceIsStillBound()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid userId = await AddUserAsync(context, Role.Student, isActive: true);
        Guid deviceId = await AddDeviceAsync(context, userId, revokedAt: null);

        var provider = new PermissionProvider(context);

        // Act
        HashSet<string> permissions = await provider.GetForUserIdAsync(userId, deviceId);

        // Assert
        permissions.ShouldBe(PermissionProvider.GetForRole(Role.Student), ignoreOrder: true);
    }

    // The token itself is still within its lifetime; what it was minted from is gone.
    [Fact]
    public async Task GetForUserIdAsync_Should_ReturnNothing_WhenTheTokensDeviceWasRevoked()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid userId = await AddUserAsync(context, Role.Student, isActive: true);
        Guid deviceId = await AddDeviceAsync(context, userId, revokedAt: new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));

        var provider = new PermissionProvider(context);

        // Act
        HashSet<string> permissions = await provider.GetForUserIdAsync(userId, deviceId);

        // Assert
        permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetForUserIdAsync_Should_ReturnNothing_WhenTheTokensDeviceBelongsToSomeoneElse()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Guid userId = await AddUserAsync(context, Role.Student, isActive: true);
        Guid othersDeviceId = await AddDeviceAsync(context, Guid.NewGuid(), revokedAt: null);

        var provider = new PermissionProvider(context);

        // Act
        HashSet<string> permissions = await provider.GetForUserIdAsync(userId, othersDeviceId);

        // Assert
        permissions.ShouldBeEmpty();
    }

    private static async Task<Guid> AddDeviceAsync(ApplicationDbContext context, Guid userId, DateTime? revokedAt)
    {
        var device = new DeviceCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceName = "laptop".AsDeviceName(),
            SecretHash = Guid.NewGuid().ToString("N"),
            CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            RevokedAt = revokedAt
        };

        context.DeviceCredentials.Add(device);
        await context.SaveChangesAsync();

        return device.Id;
    }

    private static async Task<Guid> AddUserAsync(ApplicationDbContext context, Role role, bool isActive)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com".AsEmail(),
            FirstName = "Test".AsPersonName(),
            LastName = "User".AsPersonName(),
            PasswordHash = "hash",
            Role = role,
            IndexNumber = role == Role.Student ? "19252".AsIndexNumber() : null,
            IsActive = isActive
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }
}
