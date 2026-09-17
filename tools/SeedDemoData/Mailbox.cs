using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SeedDemoData;

// Reads the verification code out of Mailpit, the mail server the compose stack runs. A new account
// is locked until the code comes back, and there is no way to skip that from outside the API - so the
// seed reads the mail exactly as a person would.
internal sealed partial class Mailbox(HttpClient http)
{
    public async Task<string?> WaitForVerificationCodeAsync(string address, DateTimeOffset sentAfter)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            MessageList? list = await http.GetFromJsonAsync<MessageList>("api/v1/messages?limit=50");

            foreach (MessageSummary message in (list?.Messages ?? []).Where(m =>
                m.Created >= sentAfter.AddSeconds(-5) &&
                m.To.Any(to => string.Equals(to.Address, address, StringComparison.OrdinalIgnoreCase))))
            {
                Message? body = await http.GetFromJsonAsync<Message>($"api/v1/message/{message.Id}");
                Match code = SixDigits().Match(body?.Text ?? string.Empty);

                if (code.Success)
                {
                    return code.Value;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return null;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();

    private sealed record MessageList(MessageSummary[] Messages);

    private sealed record MessageSummary(
        [property: JsonPropertyName("ID")] string Id,
        MessageAddress[] To,
        DateTimeOffset Created);

    private sealed record MessageAddress(string Address);

    private sealed record Message(string Text);
}
