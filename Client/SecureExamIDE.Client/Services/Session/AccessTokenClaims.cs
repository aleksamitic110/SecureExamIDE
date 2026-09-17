using System.Text;
using System.Text.Json;

namespace SecureExamIDE.Client.Services.Session;

// Reads the two claims the client needs from an access token the API issued: who it is for, and
// when it expires. Nothing here checks the signature, and nothing needs to - the client is not the
// party that has to trust the token; the server verifies it on every call.
internal static class AccessTokenClaims
{
    public static Guid? ReadUserId(string accessToken) =>
        ReadPayload(accessToken) is { } payload &&
        payload.TryGetProperty("sub", out JsonElement sub) &&
        Guid.TryParse(sub.GetString(), out Guid userId)
            ? userId
            : null;

    public static DateTimeOffset? ReadExpiry(string accessToken) =>
        ReadPayload(accessToken) is { } payload &&
        payload.TryGetProperty("exp", out JsonElement exp) &&
        exp.TryGetInt64(out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private static JsonElement? ReadPayload(string accessToken)
    {
        string[] parts = accessToken.Split('.');

        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            string base64 = parts[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));

            return document.RootElement.Clone();
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
