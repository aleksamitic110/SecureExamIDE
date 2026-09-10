using FluentValidation.TestHelper;
using Web.Api.Features.Users;

namespace Web.Api.UnitTests.Users;

public sealed class LoginUserCommandValidatorTests
{
    private readonly Login.Validator _validator = new();

    [Fact]
    public void Validator_Should_HaveError_WhenEmailIsEmpty()
    {
        var command = new Login.Command(string.Empty, "Password123", "Laptop");

        TestValidationResult<Login.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenEmailIsInvalid()
    {
        var command = new Login.Command("not-an-email", "Password123", "Laptop");

        TestValidationResult<Login.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenPasswordIsEmpty()
    {
        var command = new Login.Command("test@example.com", string.Empty, "Laptop");

        TestValidationResult<Login.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Password);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenDeviceNameIsEmpty()
    {
        var command = new Login.Command("test@example.com", "Password123", string.Empty);

        TestValidationResult<Login.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.DeviceName);
    }

    [Fact]
    public void Validator_Should_NotHaveErrors_WhenCommandIsValid()
    {
        var command = new Login.Command("test@example.com", "Password123", "Laptop");

        TestValidationResult<Login.Command> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
