using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.Review;

namespace SecureExamIDE.Client.Tests.Review;

// The professor types the sitting's code once; this is what makes that true, and what makes it stop
// being true after a while or after leaving the sitting.
public sealed class ReviewKeyCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    private ReviewKeyCache CreateCache() => new(_time);

    [Fact]
    public void Get_Should_ReturnTheKey_ForTheSittingItWasGivenFor()
    {
        // Arrange
        var sittingId = Guid.NewGuid();
        ReviewKeyCache cache = CreateCache();
        cache.Set(sittingId, (byte[])_key.Clone());

        // Act & Assert
        cache.Get(sittingId).ShouldBe(_key);
        cache.Get(Guid.NewGuid()).ShouldBeNull();
    }

    // A laptop left open on a desk must not keep a class's solutions unlocked.
    [Fact]
    public void Get_Should_ForgetTheKey_AfterHalfAnHourOfNothing()
    {
        // Arrange
        var sittingId = Guid.NewGuid();
        ReviewKeyCache cache = CreateCache();
        cache.Set(sittingId, (byte[])_key.Clone());

        // Act
        _time.Advance(TimeSpan.FromMinutes(25));
        byte[]? stillThere = cache.Get(sittingId);

        _time.Advance(TimeSpan.FromMinutes(31));
        byte[]? forgotten = cache.Get(sittingId);

        // Assert - each use puts the clock back, so marking a class keeps it alive.
        stillThere.ShouldNotBeNull();
        forgotten.ShouldBeNull();
    }

    [Fact]
    public void Clear_Should_WipeTheKey_NotOnlyForgetIt()
    {
        // Arrange
        var sittingId = Guid.NewGuid();
        byte[] key = (byte[])_key.Clone();
        ReviewKeyCache cache = CreateCache();
        cache.Set(sittingId, key);

        // Act
        cache.Clear();

        // Assert
        cache.Get(sittingId).ShouldBeNull();
        key.ShouldAllBe(b => b == 0);
    }

    // One sitting at a time: moving on forgets the previous key rather than keeping both alive.
    [Fact]
    public void Set_Should_ForgetThePreviousSittingsKey()
    {
        // Arrange
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        ReviewKeyCache cache = CreateCache();
        cache.Set(first, (byte[])_key.Clone());

        // Act
        cache.Set(second, RandomNumberGenerator.GetBytes(32));

        // Assert
        cache.Get(first).ShouldBeNull();
        cache.Get(second).ShouldNotBeNull();
    }
}
