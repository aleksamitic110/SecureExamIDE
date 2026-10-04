using System.Security.Cryptography;

namespace SecureExamIDE.Client.Services.Review;

internal sealed class ReviewKeyCache(TimeProvider timeProvider) : IReviewKeyCache
{
    private Guid _sittingId;
    private byte[]? _handInKey;
    private DateTimeOffset _lastUsed;

    public byte[]? Get(Guid sittingId)
    {
        if (_handInKey is null || _sittingId != sittingId)
        {
            return null;
        }

        if (timeProvider.GetUtcNow() - _lastUsed > Lifetime)
        {
            Clear();

            return null;
        }

        _lastUsed = timeProvider.GetUtcNow();

        return _handInKey;
    }

    // One sitting at a time: moving to another sitting's submissions forgets the previous key rather
    // than keeping a collection of them alive.
    public void Set(Guid sittingId, byte[] handInKey)
    {
        Clear();

        _sittingId = sittingId;
        _handInKey = handInKey;
        _lastUsed = timeProvider.GetUtcNow();
    }

    public void Clear()
    {
        if (_handInKey is not null)
        {
            CryptographicOperations.ZeroMemory(_handInKey);
            _handInKey = null;
        }

        _sittingId = Guid.Empty;
    }

    // Long enough to mark a class, short enough that an unattended computer forgets.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
}
