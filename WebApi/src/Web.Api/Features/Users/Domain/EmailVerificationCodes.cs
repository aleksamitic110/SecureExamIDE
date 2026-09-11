using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Web.Api.Features.Users;

// Everything about the codes themselves, in one place so that registration and a resend can never
// produce, store or word them differently.
internal static class EmailVerificationCodes
{
    // Six digits, because a student types it into the desktop client by hand.
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    // The user id is mixed in so that the same six digits never hash the same for two accounts.
    public static string Hash(Guid userId, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{userId:N}:{code}"))));

    // Compared in constant time, like the professor registration code.
    public static bool Matches(EmailVerificationCode stored, string code) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stored.CodeHash),
            Encoding.ASCII.GetBytes(Hash(stored.UserId, code)));

    public static string Body(string code, int lifetimeMinutes) => string.Create(
        CultureInfo.InvariantCulture,
        $"Your SecureExamIDE verification code is: {code}\n\nThe code is valid for {lifetimeMinutes} minutes. Enter it in the SecureExamIDE application.\n\nIf you did not create an account, you can ignore this message.");

    public const string Subject = "Your SecureExamIDE verification code";
}
