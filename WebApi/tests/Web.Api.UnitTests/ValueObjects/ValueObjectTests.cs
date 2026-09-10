using Web.Api.Common;
using Web.Api.Common.ValueObjects;
using Web.Api.Features.Exams;

namespace Web.Api.UnitTests.ValueObjects;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("Aleksa@Example.COM", "aleksa@example.com")]
    [InlineData("  aleksa@example.com  ", "aleksa@example.com")]
    [InlineData("aleksa@example.com", "aleksa@example.com")]
    public void Email_Should_NormalizeCasingAndWhitespace(string input, string expected)
    {
        // Act
        Result<Email> result = Email.Create(input);

        // Assert - normalising here is what stops "Aleksa@x.com" registering and then failing to
        // match at login.
        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(expected);
    }

    [Fact]
    public void Email_Should_CompareEqual_RegardlessOfOriginalCasing()
    {
        // Act
        Email upper = Email.Create("Aleksa@Example.com").Value;
        Email lower = Email.Create("aleksa@example.com").Value;

        // Assert
        upper.ShouldBe(lower);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("aleksa@")]
    public void Email_Should_ReturnFailure_WhenMalformed(string? input)
    {
        // Act
        Result<Email> result = Email.Create(input);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    // Value objects must not throw for expected failures - that is the convention the rest of the
    // codebase follows, and the original Email implementation broke it.
    [Fact]
    public void Email_Should_ReturnAProblemError_SoTheEndpointRespondsWith400()
    {
        // Act
        Result<Email> result = Email.Create("not-an-email");

        // Assert
        result.Error.Type.ShouldBe(ErrorType.Problem);
    }

    [Theory]
    [InlineData("1925")]
    [InlineData("19252")]
    [InlineData("192520")]
    public void IndexNumber_Should_AcceptFourToSixDigits(string input) =>
        IndexNumber.Create(input).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("19a52")]
    [InlineData("2019/0252")]
    [InlineData(null)]
    public void IndexNumber_Should_RejectAnythingElse(string? input) =>
        IndexNumber.Create(input).IsFailure.ShouldBeTrue();

    [Fact]
    public void Sha256Hash_Should_AcceptSixtyFourLowercaseHexCharacters() =>
        Sha256Hash.Create("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
            .IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("ba7816bf")]
    [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Sha256Hash_Should_RejectUppercaseTruncatedOrNonHex(string input) =>
        Sha256Hash.Create(input).IsFailure.ShouldBeTrue();

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("folder/task.pdf")]
    [InlineData("folder\\task.pdf")]
    [InlineData("..")]
    public void FileName_Should_RejectAnythingThatCouldBeReadAsAPath(string input) =>
        FileName.Create(input).IsFailure.ShouldBeTrue();

    [Fact]
    public void FileName_Should_AcceptAnOrdinaryName() =>
        FileName.Create("task.pdf").IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    public void ContentType_Should_AcceptAMediaType(string input) =>
        ContentType.Create(input).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("pdf")]
    [InlineData("/pdf")]
    [InlineData("application/")]
    public void ContentType_Should_RejectAMalformedMediaType(string input) =>
        ContentType.Create(input).IsFailure.ShouldBeTrue();

    [Fact]
    public void ExamTitle_Should_RejectAValueOverTheLimit() =>
        ExamTitle.Create(new string('x', ExamTitle.MaxLength + 1)).IsFailure.ShouldBeTrue();

    [Fact]
    public void ExamTitle_Should_AcceptAValueAtTheLimit() =>
        ExamTitle.Create(new string('x', ExamTitle.MaxLength)).IsSuccess.ShouldBeTrue();

    [Fact]
    public void PersonName_Should_TrimSurroundingWhitespace() =>
        PersonName.Create("  Aleksa  ").Value.Value.ShouldBe("Aleksa");
}
