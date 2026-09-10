using Web.Api.Common.Crypto;

namespace Web.Api.UnitTests.Crypto;

public sealed class OneTimeCodeProviderTests
{
    [Fact]
    public void Generate_Should_ProduceAReadableGroupedCode()
    {
        // Arrange
        var provider = new OneTimeCodeProvider();

        // Act
        string code = provider.Generate();

        // Assert
        code.Length.ShouldBe(24);
        code.Split('-').Length.ShouldBe(5);
        code.Split('-').ShouldAllBe(group => group.Length == 4);
    }

    // I, L, O and U are left out so a code read aloud or copied by hand has nothing ambiguous in
    // it, and cannot accidentally spell a word.
    [Fact]
    public void Generate_Should_AvoidAmbiguousCharacters()
    {
        // Arrange
        var provider = new OneTimeCodeProvider();

        // Act
        IEnumerable<string> codes = Enumerable.Range(0, 200).Select(_ => provider.Generate());

        // Assert
        string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ-";
        codes.SelectMany(code => code).ShouldAllBe(character => alphabet.Contains(character));
    }

    [Fact]
    public void Generate_Should_NotRepeatItself()
    {
        // Arrange
        var provider = new OneTimeCodeProvider();

        // Act
        List<string> codes = [.. Enumerable.Range(0, 500).Select(_ => provider.Generate())];

        // Assert
        codes.Distinct().Count().ShouldBe(codes.Count);
    }

    [Fact]
    public void Hash_Should_ProduceAStorableDigestRatherThanTheCode()
    {
        // Arrange
        var provider = new OneTimeCodeProvider();
        string code = provider.Generate();

        // Act
        string hash = provider.Hash(code);

        // Assert
        hash.Length.ShouldBe(64);
        hash.ShouldAllBe(character => "0123456789abcdef".Contains(character));
        hash.ShouldNotContain(code.Replace("-", "", StringComparison.Ordinal));
    }

    // The professor may type the code back in any of these forms; all of them have to be
    // recognised, because the key derivation folds them the same way too.
    [Theory]
    [InlineData("7QK2-M9XB-4TVA-0HRE-JW3N", "7qk2m9xb4tva0hrejw3n")]
    [InlineData("7QK2-M9XB-4TVA-0HRE-JW3N", "7QK2 M9XB 4TVA OHRE JW3N")]
    public void Hash_Should_MatchForEquivalentlyTypedCodes(string issued, string typed)
    {
        // Arrange
        var provider = new OneTimeCodeProvider();

        // Act & Assert
        provider.Hash(typed).ShouldBe(provider.Hash(issued));
    }

    [Fact]
    public void Hash_Should_DifferForDifferentCodes()
    {
        // Arrange
        var provider = new OneTimeCodeProvider();

        // Act & Assert
        provider.Hash("7QK2-M9XB-4TVA-0HRE-JW3N")
            .ShouldNotBe(provider.Hash("7QK2-M9XB-4TVA-0HRE-JW3P"));
    }
}
