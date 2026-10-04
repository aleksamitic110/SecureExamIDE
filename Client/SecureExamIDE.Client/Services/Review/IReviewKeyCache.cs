namespace SecureExamIDE.Client.Services.Review;

// Holds the hand-in key of the sitting being reviewed, so the professor types the one-time code once
// rather than once per student. It lives in memory only, for **one sitting at a time**, and is
// forgotten when the professor leaves the sitting or after a spell of doing nothing - a laptop left
// open on a desk should not keep a class's solutions unlocked.
public interface IReviewKeyCache
{
    // The key for this sitting, or null when the code has not been given yet or has been forgotten.
    byte[]? Get(Guid sittingId);

    void Set(Guid sittingId, byte[] handInKey);

    void Clear();
}
