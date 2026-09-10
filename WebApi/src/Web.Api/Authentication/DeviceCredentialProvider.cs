using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Web.Api.Authentication;

internal sealed class DeviceCredentialProvider : IDeviceCredentialProvider
{
    public string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretSizeInBytes));

    // A plain, unsalted SHA-256 - deliberately, and unlike PasswordHasher's 500 000 PBKDF2 rounds.
    // The secret is 256 bits of cryptographic randomness rather than a human-chosen password, so
    // there is no dictionary to attack and key stretching would buy nothing. What a fast, salt-free
    // digest does buy is a deterministic value that can be indexed, letting the server find the
    // credential by hash instead of hashing every row on every request.
    public string Hash(string secret) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private const int SecretSizeInBytes = 32;
}
