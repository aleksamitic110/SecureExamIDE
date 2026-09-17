using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Tests.Fakes;

namespace SecureExamIDE.Client.Tests.Session;

public sealed class AccessTokenClaimsTests
{
    [Fact]
    public void Claims_Should_BeReadFromTheTokenPayload()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var expiresAt = new DateTimeOffset(2026, 9, 15, 10, 15, 0, TimeSpan.Zero);
        string token = TestTokens.Create(userId, expiresAt);

        // Act & Assert
        AccessTokenClaims.ReadUserId(token).ShouldBe(userId);
        AccessTokenClaims.ReadExpiry(token).ShouldBe(expiresAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.%%%.c")]
    public void Claims_Should_BeNull_ForSomethingThatIsNotAToken(string token)
    {
        // Act & Assert
        AccessTokenClaims.ReadUserId(token).ShouldBeNull();
        AccessTokenClaims.ReadExpiry(token).ShouldBeNull();
    }
}
