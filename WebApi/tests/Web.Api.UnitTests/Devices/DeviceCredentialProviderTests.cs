using Web.Api.Authentication;

namespace Web.Api.UnitTests.Devices;

public sealed class DeviceCredentialProviderTests
{
    private readonly DeviceCredentialProvider _provider = new();

    [Fact]
    public void Generate_Should_ProduceADistinctSecretEveryTime()
    {
        // Act
        string[] secrets = [.. Enumerable.Range(0, 100).Select(_ => _provider.Generate())];

        // Assert
        secrets.Distinct(StringComparer.Ordinal).Count().ShouldBe(secrets.Length);
    }

    [Fact]
    public void Generate_Should_ProduceA256BitSecret()
    {
        // Act
        string secret = _provider.Generate();

        // Assert - 32 bytes base64url-encoded, so no padding and 43 characters.
        secret.Length.ShouldBe(43);
    }

    [Fact]
    public void Hash_Should_BeDeterministic_SoTheSecretCanBeFoundByIndexedLookup()
    {
        // Arrange
        string secret = _provider.Generate();

        // Act
        string first = _provider.Hash(secret);
        string second = _provider.Hash(secret);

        // Assert
        first.ShouldBe(second);
    }

    [Fact]
    public void Hash_Should_NotContainThePlaintextSecret()
    {
        // Arrange
        string secret = _provider.Generate();

        // Act
        string hash = _provider.Hash(secret);

        // Assert
        hash.ShouldNotBe(secret);
        hash.ShouldNotContain(secret, Case.Sensitive);
    }

    [Fact]
    public void Hash_Should_DifferForDifferentSecrets()
    {
        // Act
        string first = _provider.Hash(_provider.Generate());
        string second = _provider.Hash(_provider.Generate());

        // Assert
        first.ShouldNotBe(second);
    }
}
