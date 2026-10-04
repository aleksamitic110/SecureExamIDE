using System.Security.Cryptography;
using System.Text;

namespace SecureExamIDE.Client.Services.ActivityLog;

// One line per event, each sealed on its own with AES-256-GCM so the file can be appended to as the
// exam goes on, and so a crash can never leave a half-written record that spoils the rest. The
// sealing and the chain live in ActivityLogReader, which the professor's review shares; this class is
// the student's side of it - where the file is, and what happens when it cannot be written.
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
            _previousDigest = ActivityLogReader.DigestOf(recorded);
        }
    }

    public void Write(ActivityKind kind, string? detail = null)
    {
        lock (_writing)
        {
            var recorded = new ActivityEvent(_sequence + 1, _timeProvider.GetUtcNow(), kind, detail);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(
                    _path,
                    ActivityLogReader.Seal(recorded, _previousDigest, _sittingId, _key) + Environment.NewLine,
                    Encoding.ASCII);

                _sequence = recorded.Sequence;
                _previousDigest = ActivityLogReader.DigestOf(recorded);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The exam matters more than its log: a disk that cannot be written to must not stop
                // a student working. The gap in the sequence is itself a record that this happened.
            }
        }
    }

    public IReadOnlyList<ActivityEvent> Read() =>
        File.Exists(_path)
            ? ActivityLogReader.Read(File.ReadLines(_path), _sittingId, _key).Events
            : [];

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
