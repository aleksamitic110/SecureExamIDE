using System.Globalization;
using System.Text;

namespace SecureExamIDE.Client.Tests.Fakes;

// Builds tokens shaped like the API's JWTs. The signature is not real; the client never checks it.
internal static class TestTokens
{
    public static string Create(Guid userId, DateTimeOffset expiresAt)
    {
        string payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"sub\":\"{userId}\",\"exp\":{expiresAt.ToUnixTimeSeconds()}}}");

        return $"{Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")}.{Encode(payload)}.not-a-real-signature";
    }

    private static string Encode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
