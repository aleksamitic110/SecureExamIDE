using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureExamIDE.Client.Services.ActivityLog;

// Reading the log back, shared by the two sides that do it: the student's client, which continues a
// log after a restart, and the **professor's review**, which reads the log that arrived with a
// submission. One implementation, so the chain is checked the same way in both places.
//
// Each line is sealed on its own, and two things tie it to its place: the sequence number and the
// sitting's id are the authenticated data, and every event carries the digest of the one before it.
// Reading therefore stops at the first line that does not fit, and says so - a log that was tampered
// with must not read as if it were whole.
internal static class ActivityLogReader
{
    public static ActivityLogContents Read(IEnumerable<string> lines, Guid sittingId, byte[] key)
    {
        // Counted in full first, so a reader that stops at a broken line can still say how many lines
        // the log claimed to have - "4 events of 7 lines" is what tells a professor what is missing.
        List<string> written = [.. lines.Where(line => line.Length > 0)];

        List<ActivityEvent> events = [];
        string previousDigest = string.Empty;

        for (int sequence = 1; sequence <= written.Count; sequence++)
        {
            StoredEvent? stored = Open(written[sequence - 1], sequence, sittingId, key);

            if (stored is null || stored.Previous != previousDigest || stored.Event.Sequence != sequence)
            {
                return new ActivityLogContents(events, IsComplete: false, written.Count);
            }

            events.Add(stored.Event);
            previousDigest = DigestOf(stored.Event);
        }

        return new ActivityLogContents(events, IsComplete: true, written.Count);
    }

    // What a new line has to carry to continue the chain.
    public static string DigestOf(ActivityEvent recorded) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(recorded, JsonOptions)));

    public static string Seal(ActivityEvent recorded, string previousDigest, Guid sittingId, byte[] key)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new StoredEvent(recorded, previousDigest), JsonOptions);

        try
        {
            byte[] sealedBytes = new byte[NonceSize + TagSize + plaintext.Length];

            RandomNumberGenerator.Fill(sealedBytes.AsSpan(0, NonceSize));

            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(
                sealedBytes.AsSpan(0, NonceSize),
                plaintext,
                sealedBytes.AsSpan(NonceSize + TagSize),
                sealedBytes.AsSpan(NonceSize, TagSize),
                AssociatedData(sittingId, recorded.Sequence));

            return Convert.ToBase64String(sealedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static StoredEvent? Open(string line, int sequence, Guid sittingId, byte[] key)
    {
        try
        {
            byte[] sealedBytes = Convert.FromBase64String(line);

            if (sealedBytes.Length <= NonceSize + TagSize)
            {
                return null;
            }

            byte[] plaintext = new byte[sealedBytes.Length - NonceSize - TagSize];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                sealedBytes.AsSpan(0, NonceSize),
                sealedBytes.AsSpan(NonceSize + TagSize),
                sealedBytes.AsSpan(NonceSize, TagSize),
                plaintext,
                AssociatedData(sittingId, sequence));

            return JsonSerializer.Deserialize<StoredEvent>(plaintext, JsonOptions);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static byte[] AssociatedData(Guid sittingId, int sequence) =>
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"SecureExamIDE activity v1|{sittingId:N}|{sequence}"));

    private sealed record StoredEvent(ActivityEvent Event, string Previous);

    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Kinds are written as their names, so whoever opens the log reads "ExamWindowLeft" and not "9".
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

// IsComplete is false when a line did not open or the chain broke: the events before that point are
// still what they say they are, and everything after it is not to be trusted.
public sealed record ActivityLogContents(IReadOnlyList<ActivityEvent> Events, bool IsComplete, int LineCount);
