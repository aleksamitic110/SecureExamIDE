using Web.Api.Features.Users;

namespace Web.Api.Authentication;

public interface ITokenProvider
{
    string Create(User user);

    // Mints the short-lived token a client gets in exchange for a valid device credential. It is
    // deliberately shorter-lived than a login token and carries the device it was minted for.
    string CreateForDevice(User user, Guid deviceCredentialId);

    string GenerateRefreshToken();
}
