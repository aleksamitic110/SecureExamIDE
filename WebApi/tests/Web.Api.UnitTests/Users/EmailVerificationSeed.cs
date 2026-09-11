using Web.Api.Database;
using Web.Api.Features.Users;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Users;

// Builds an account and, optionally, the verification code outstanding for it, for the tests of
// the slices that verify an address or resend its code.
internal static class EmailVerificationSeed
{
    public const string Address = "student@example.com";
    public const string Code = "123456";

    public static async Task<User> SeedUserAsync(ApplicationDbContext context, bool verified = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = Address.AsEmail(),
            FirstName = "Test".AsPersonName(),
            LastName = "Student".AsPersonName(),
            PasswordHash = "hash",
            Role = Role.Student,
            IndexNumber = "19252".AsIndexNumber(),
            IsActive = true,
            EmailVerifiedAt = verified ? DateTime.UtcNow : null
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user;
    }

    public static async Task<EmailVerificationCode> SeedCodeAsync(
        ApplicationDbContext context,
        Guid userId,
        DateTime createdAt,
        DateTime expiresAt,
        int failedAttempts = 0)
    {
        var code = new EmailVerificationCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = EmailVerificationCodes.Hash(userId, Code),
            ExpiresAt = expiresAt,
            FailedAttempts = failedAttempts,
            CreatedAt = createdAt
        };

        context.EmailVerificationCodes.Add(code);
        await context.SaveChangesAsync();

        return code;
    }
}
