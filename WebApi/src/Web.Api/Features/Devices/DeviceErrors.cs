using Web.Api.Common;

namespace Web.Api.Features.Devices;

public static class DeviceErrors
{
    public static Error NotFound(Guid deviceId) => Error.NotFound(
        "Devices.NotFound",
        $"The device with the Id = '{deviceId}' was not found");

    // Deliberately the same error for an unknown secret, a revoked credential and a deactivated
    // owner, so a caller holding a stolen credential cannot learn which of the three applies.
    public static readonly Error InvalidCredential = Error.Unauthorized(
        "Devices.InvalidCredential",
        "The provided device credential is invalid or has been revoked");

    public static readonly Error AlreadyRevoked = Error.Conflict(
        "Devices.AlreadyRevoked",
        "The device credential has already been revoked");
}
