using System.Security.Cryptography;
using System.Text;
using Web.Api.Common.Crypto;

namespace Web.Api.UnitTests.Crypto;

public sealed class CryptoServiceTests
{
    private const string Code = "7QK2-M9XB-4TVA-0HRE-JW3N";

    private static readonly byte[] Exam = Encoding.UTF8.GetBytes(
        "Task 1: implement a red-black tree.\nTask 2: prove the height bound.");

    // The claim the entire system rests on: the header travels with the package, and the code
    // alone turns it back into the exam - no server, no network.
    [Fact]
    public void AClientHoldingOnlyThePackageHeaderAndTheCode_Should_RecoverTheExam()
    {
        // Arrange
        var cryptoService = new CryptoService();

        // Act
        SealedPackage sealedPackage = cryptoService.Seal(Exam, Code);
        byte[] recovered = PackageReader.Open(sealedPackage.Ciphertext, sealedPackage.Header, Code);

        // Assert
        recovered.ShouldBe(Exam);
    }

    [Fact]
    public void Seal_Should_NotLeaveThePlaintextInTheCiphertext()
    {
        // Arrange
        var cryptoService = new CryptoService();

        // Act
        SealedPackage sealedPackage = cryptoService.Seal(Exam, Code);

        // Assert
        sealedPackage.Ciphertext.ShouldNotBe(Exam);
        Encoding.UTF8.GetString(sealedPackage.Ciphertext).ShouldNotContain("red-black");
    }

    // GCM authentication is what makes a wrong code fail cleanly instead of producing plausible
    // rubbish that a student might mistake for a corrupted download.
    [Fact]
    public void AWrongCode_Should_FailToOpenThePackage()
    {
        // Arrange
        var cryptoService = new CryptoService();
        SealedPackage sealedPackage = cryptoService.Seal(Exam, Code);

        // Act & Assert
        Should.Throw<AuthenticationTagMismatchException>(() =>
            PackageReader.Open(sealedPackage.Ciphertext, sealedPackage.Header, "7QK2-M9XB-4TVA-0HRE-JW3P"));
    }

    [Fact]
    public void ATamperedPackage_Should_FailToOpen()
    {
        // Arrange
        var cryptoService = new CryptoService();
        SealedPackage sealedPackage = cryptoService.Seal(Exam, Code);
        sealedPackage.Ciphertext[0] ^= 0xFF;

        // Act & Assert
        Should.Throw<AuthenticationTagMismatchException>(() =>
            PackageReader.Open(sealedPackage.Ciphertext, sealedPackage.Header, Code));
    }

    // Casing, the dashes and the characters people confuse when copying a code off a whiteboard
    // must all land on the same key, or a correct code would be rejected on exam day.
    [Theory]
    [InlineData("7qk2-m9xb-4tva-0hre-jw3n")]
    [InlineData("7QK2 M9XB 4TVA 0HRE JW3N")]
    [InlineData("7QK2M9XB4TVA0HREJW3N")]
    [InlineData("7QK2-M9XB-4TVA-OHRE-JW3N")]
    public void AnEquivalentlyTypedCode_Should_OpenThePackage(string typedCode)
    {
        // Arrange
        var cryptoService = new CryptoService();
        SealedPackage sealedPackage = cryptoService.Seal(Exam, Code);

        // Act
        byte[] recovered = PackageReader.Open(sealedPackage.Ciphertext, sealedPackage.Header, typedCode);

        // Assert
        recovered.ShouldBe(Exam);
    }

    // Sealing the same exam for two sittings must not produce the same bytes, or the second
    // package would be openable by anyone who kept the first one's code.
    [Fact]
    public void SealingTwice_Should_ProduceDifferentCiphertextAndSalt()
    {
        // Arrange
        var cryptoService = new CryptoService();

        // Act
        SealedPackage first = cryptoService.Seal(Exam, Code);
        SealedPackage second = cryptoService.Seal(Exam, Code);

        // Assert
        first.Ciphertext.ShouldNotBe(second.Ciphertext);
        first.Header.KdfSalt.ShouldNotBe(second.Header.KdfSalt);
        first.Header.PackageNonce.ShouldNotBe(second.Header.PackageNonce);
        first.Header.WrappedKey.ShouldNotBe(second.Header.WrappedKey);
    }

    [Fact]
    public void Seal_Should_RecordTheParametersAClientNeedsToDeriveTheKey()
    {
        // Arrange
        var cryptoService = new CryptoService();

        // Act
        PackageHeader header = cryptoService.Seal(Exam, Code).Header;

        // Assert
        header.Version.ShouldBe(PackageHeader.CurrentVersion);
        header.Algorithm.ShouldBe("AES-256-GCM");
        header.Kdf.ShouldBe("Argon2id");
        header.KdfIterations.ShouldBeGreaterThan(0);
        header.KdfMemoryKib.ShouldBeGreaterThanOrEqualTo(65_536);
        header.KdfParallelism.ShouldBeGreaterThan(0);
        Convert.FromBase64String(header.KdfSalt).Length.ShouldBe(16);
        Convert.FromBase64String(header.PackageNonce).Length.ShouldBe(12);
        Convert.FromBase64String(header.PackageTag).Length.ShouldBe(16);
        Convert.FromBase64String(header.WrappedKey).Length.ShouldBe(32);
    }
}
