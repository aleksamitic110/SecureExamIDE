namespace SecureExamIDE.Client.Services.Api;

// The JSON shapes of the account endpoints, mirroring the API's own request and response records.

public sealed record RegisterRequest(
    string Email,
    string FirstName,
    string LastName,
    string Password,
    UserRole Role,
    string? IndexNumber,
    string? ProfessorRegistrationCode,
    string DeviceName);

public sealed record RegisterResponse(Guid UserId, Guid DeviceId, string DeviceCredential);

public sealed record VerifyEmailRequest(string Email, string Code);

public sealed record ResendVerificationCodeRequest(string Email);

public sealed record LoginRequest(string Email, string Password, string DeviceName);

public sealed record LoginResponse(string AccessToken, string RefreshToken, Guid DeviceId, string DeviceCredential);

public sealed record DeviceTokenRequest(string DeviceCredential);

public sealed record DeviceTokenResponse(string AccessToken);

public sealed record UserProfile(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    UserRole Role,
    string? IndexNumber);
