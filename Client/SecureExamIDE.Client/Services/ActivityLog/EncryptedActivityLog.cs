using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecureExamIDE.Client.Services.ActivityLog;

// One line per event, each sealed on its own with AES-256-GCM so the file can be appended to as the
// exam goes on, and so a crash can never leave a half-written record that spoils the rest.
//
//   line = base64( nonce (12) | tag (16) | ciphertext )
//
// Two things make the log evidence rather than a note:
//
// - Each event carries the **digest of the event before it**, so removing or reordering lines shows.
//   The chain is continued when the log is reopened after a restart.
// - The sequence number and the sitting's id are the **authenticated data** of each line, so a line
//   cannot be moved to another position, or taken from another sitting, without the tag failing.
//
// Nothing stops a student deleting the whole file; what that costs them is the evidence that they
// worked honestly, and the submission says how many events it carried.
internal sealed class EncryptedActivityLog : IActivityLog
{
    private readonly string _path;
    private readonly Guid _sittingId;
    private readonly byte[] _key;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _writing = new();

    private int _sequence;
    private string _previousDigest;

    public EncryptedActivityLog(string path, Guid sittingId, byte[] key, TimeProvider timeProvider)
    {
        _path = path;
        _sittingId = sittingId;
        _key = (byte[])key.Clone();
        _timeProvider = timeProvider;
        _previousDigest = string.Empty;

        // Continuing a log from before a restart: the chain picks up where it left off.
        foreach (ActivityEvent recorded in Read())
        {
            _sequence = recorded.Sequence;
            _previousDigest = DigestOf(recorded);
        }
    }

    public void Write(ActivityKind kind, string? detail = null)
    {
        lock (_writing)
        {
            var recorded = new ActivityEvent(_sequence + 1, _timeProvider.GetUtcNow(), kind, detail);
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new StoredEvent(recorded, _previousDigest), JsonOptions);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, Seal(plaintext, recorded.Sequence) + Environment.NewLine, Encoding.ASCII);

                _sequence = recorded.Sequence;
                _previousDigest = DigestOf(recorded);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The exam matters more than its log: a disk that cannot be written to must not stop
                // a student working. The gap in the sequence is itself a record that this happened.
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public IReadOnlyList<ActivityEvent> Read()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        List<ActivityEvent> events = [];
        int sequence = 0;
        string previousDigest = string.Empty;

        foreach (string line in File.ReadLines(_path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            sequence++;

            StoredEvent? stored = Open(line, sequence);

            // A line that does not open, or whose chain does not match, ends the reading: everything
            // after it is unreliable, and saying so is the point of the chain.
            if (stored is null || stored.Previous != previousDigest || stored.Event.Sequence != sequence)
            {
                break;
            }

            events.Add(stored.Event);
            previousDigest = DigestOf(stored.Event);
        }

        return events;
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);

    private string Seal(byte[] plaintext, int sequence)
    {
        byte[] sealedBytes = new byte[NonceSize + TagSize + plaintext.Length];

        RandomNumberGenerator.Fill(sealedBytes.AsSpan(0, NonceSize));

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(
            sealedBytes.AsSpan(0, NonceSize),
            plaintext,
            sealedBytes.AsSpan(NonceSize + TagSize),
            sealedBytes.AsSpan(NonceSize, TagSize),
            AssociatedData(sequence));

        return Convert.ToBase64String(sealedBytes);
    }

    private StoredEvent? Open(string line, int sequence)
    {
        try
        {
            byte[] sealedBytes = Convert.FromBase64String(line);

            if (sealedBytes.Length <= NonceSize + TagSize)
            {
                return null;
            }

            byte[] plaintext = new byte[sealedBytes.Length - NonceSize - TagSize];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                sealedBytes.AsSpan(0, NonceSize),
                sealedBytes.AsSpan(NonceSize + TagSize),
                sealedBytes.AsSpan(NonceSize, TagSize),
                plaintext,
                AssociatedData(sequence));

            return JsonSerializer.Deserialize<StoredEvent>(plaintext, JsonOptions);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    private byte[] AssociatedData(int sequence) =>
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"SecureExamIDE activity v1|{_sittingId:N}|{sequence}"));

    private static string DigestOf(ActivityEvent recorded) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(recorded, JsonOptions)));

    private sealed record StoredEvent(ActivityEvent Event, string Previous);

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
