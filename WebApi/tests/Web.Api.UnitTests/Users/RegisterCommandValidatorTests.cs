using FluentValidation.TestHelper;
using Web.Api.Features.Users;

namespace Web.Api.UnitTests.Users;

public sealed class RegisterCommandValidatorTests
{
    private readonly Register.Validator _validator = new();

    private static Register.Command StudentCommand =>
        new("student@example.com", "Test", "Student", "Password123", Role.Student, "19252", null, "Laptop");

    private static Register.Command ProfessorCommand =>
        new("professor@example.com", "Test", "Professor", "Password123", Role.Professor, null, "code", "Office PC");

    [Fact]
    public void Validator_Should_HaveError_WhenStudentHasNoIndexNumber()
    {
        Register.Command command = StudentCommand with { IndexNumber = null };

        TestValidationResult<Register.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.IndexNumber);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("19a52")]
    [InlineData("2019/0252")]
    public void Validator_Should_HaveError_WhenIndexNumberIsNotFourToSixDigits(string indexNumber)
    {
        Register.Command command = StudentCommand with { IndexNumber = indexNumber };

        TestValidationResult<Register.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.IndexNumber);
    }

    [Theory]
    [InlineData("1925")]
    [InlineData("19252")]
    [InlineData("192520")]
    public void Validator_Should_NotHaveErrors_WhenIndexNumberIsFourToSixDigits(string indexNumber)
    {
        Register.Command command = StudentCommand with { IndexNumber = indexNumber };

        TestValidationResult<Register.Command> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_Should_HaveError_WhenStudentSuppliesProfessorRegistrationCode()
    {
        Register.Command command = StudentCommand with { ProfessorRegistrationCode = "code" };

        TestValidationResult<Register.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.ProfessorRegistrationCode);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenProfessorSuppliesIndexNumber()
    {
        Register.Command command = ProfessorCommand with { IndexNumber = "19252" };

        TestValidationResult<Register.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.IndexNumber);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenProfessorHasNoRegistrationCode()
    {
        Register.Command command = ProfessorCommand with { ProfessorRegistrationCode = null };

        TestValidationResult<Register.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.ProfessorRegistrationCode);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenRoleIsNotDefined()
    {
        Register.Command command = StudentCommand with { Role = (Role)99 };

        TestValidationResult<Register.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Role);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenDeviceNameIsEmpty()
    {
        Register.Command command = StudentCommand with { DeviceName = string.Empty };

        TestValidationResult<Register.Command> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.DeviceName);
    }

    [Fact]
    public void Validator_Should_NotHaveErrors_WhenProfessorCommandIsValid()
    {
        TestValidationResult<Register.Command> result = _validator.TestValidate(ProfessorCommand);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
