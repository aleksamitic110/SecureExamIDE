using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Devices;

// A long-lived secret bound to one machine. It is what lets a student submit work without ever
// logging in: the client presents the credential, the server mints a short-lived access token for
// that one exchange. Credentials never expire - they are lost only by wiping the client's secure
// storage or by revoking the row here.
public sealed class DeviceCredential : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    // Human-readable name so the owner can recognise the machine in the device list.
    public DeviceName DeviceName { get; set; }

    // SHA-256 of the secret. The plaintext is returned exactly once, at issue time, and is never
    // stored - a database dump therefore yields nothing that can be replayed. Kept as a plain
    // string for the same reason as User.PasswordHash: it is opaque crypto material.
    public string SecretHash { get; set; }

    public DateTime CreatedAt { get; set; }

    // Null while the credential is usable. Set once, and never cleared.
    public DateTime? RevokedAt { get; set; }
}
