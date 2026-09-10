using System.Security.Claims;

namespace Web.Api.Authentication;

internal static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal? principal)
    {
        string? userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(userId, out Guid parsedUserId) ?
            parsedUserId :
            throw new ApplicationException("User id is unavailable");
    }

    // Only a device token carries this claim, so its absence is how an ordinary login is
    // recognised rather than an error.
    public static Guid? GetDeviceId(this ClaimsPrincipal? principal)
    {
        string? deviceId = principal?.FindFirstValue(DeviceClaimName);

        return Guid.TryParse(deviceId, out Guid parsedDeviceId) ? parsedDeviceId : null;
    }

    private const string DeviceClaimName = "device_id";
}
