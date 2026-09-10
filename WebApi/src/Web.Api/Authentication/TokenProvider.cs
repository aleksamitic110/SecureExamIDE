using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Web.Api.Features.Users;

namespace Web.Api.Authentication;

internal sealed class TokenProvider(IConfiguration configuration) : ITokenProvider
{
    public string Create(User user) =>
        Create(user, configuration.GetValue<int>("Jwt:ExpirationInMinutes"), extraClaims: []);

    public string CreateForDevice(User user, Guid deviceCredentialId)
    {
        // appsettings.json ships every Jwt value as an unset placeholder, so 0 means "not
        // configured" here just as an empty string does for the secret.
        int configured = configuration.GetValue<int>("Jwt:DeviceTokenExpirationInMinutes");
        int expirationInMinutes = configured > 0 ? configured : DefaultDeviceTokenExpirationInMinutes;

        // Naming the device in the token means a submission can be traced back to the machine it
        // came from, and lets a revoked device be recognised even before the token expires.
        return Create(user, expirationInMinutes, [new Claim(DeviceClaimName, deviceCredentialId.ToString())]);
    }

    public string GenerateRefreshToken()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(RefreshTokenSizeInBytes);

        return Convert.ToBase64String(randomBytes);
    }

    private string Create(User user, int expirationInMinutes, Claim[] extraClaims)
    {
        string secretKey = configuration["Jwt:Secret"]!;
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email.Value),

            // Carried for the client's benefit only - it decides which screens to show. The API
            // never trusts this claim for authorization; PermissionProvider reads the role from the
            // database on every protected request.
            new Claim(RoleClaimName, user.Role.ToString()),
            .. extraClaims
        ];

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expirationInMinutes),
            SigningCredentials = credentials,
            Issuer = configuration["Jwt:Issuer"],
            Audience = configuration["Jwt:Audience"]
        };

        var handler = new JsonWebTokenHandler();

        return handler.CreateToken(tokenDescriptor);
    }

    private const string RoleClaimName = "role";
    private const string DeviceClaimName = "device_id";
    private const int RefreshTokenSizeInBytes = 32;
    private const int DefaultDeviceTokenExpirationInMinutes = 15;
}
