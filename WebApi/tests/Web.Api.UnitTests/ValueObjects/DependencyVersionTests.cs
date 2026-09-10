using Web.Api.Common;
using Web.Api.Features.Exams;

namespace Web.Api.UnitTests.ValueObjects;

public sealed class DependencyVersionTests
{
    [Theory]
    [InlineData("13.2.0")]
    [InlineData("21.0.2-tem")]
    [InlineData("1.0.0+build.7")]
    [InlineData("v2_1")]
    public void Create_Should_Succeed_ForVersionsUsingTheAllowedCharacters(string value)
    {
        Result<DependencyVersion> result = DependencyVersion.Create(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value);
    }

    // Whitespace is what would let the same dependency be stored twice under two rows the unique
    // index cannot see as equal.
    [Theory]
    [InlineData("13.2.0 beta")]
    [InlineData("13/2/0")]
    [InlineData("13.2.0!")]
    public void Create_Should_Fail_ForVersionsContainingDisallowedCharacters(string value)
    {
        Result<DependencyVersion> result = DependencyVersion.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DependencyVersion.InvalidFormat");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_Should_Fail_WhenValueIsMissing(string? value)
    {
        Result<DependencyVersion> result = DependencyVersion.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DependencyVersion.Empty");
    }

    [Fact]
    public void Create_Should_TrimSurroundingWhitespace()
    {
        Result<DependencyVersion> result = DependencyVersion.Create("  13.2.0  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("13.2.0");
    }

    [Fact]
    public void Create_Should_Fail_WhenValueIsTooLong()
    {
        Result<DependencyVersion> result = DependencyVersion.Create(new string('1', DependencyVersion.MaxLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DependencyVersion.TooLong");
    }
}
