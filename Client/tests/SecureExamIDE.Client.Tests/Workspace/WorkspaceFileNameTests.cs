using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Workspace;

namespace SecureExamIDE.Client.Tests.Workspace;

public sealed class WorkspaceFileNameTests
{
    [Theory]
    [InlineData("main.c")]
    [InlineData("Main.java")]
    [InlineData("list_utils.h")]
    [InlineData("solution-2.py")]
    [InlineData("Makefile")]
    public void Validate_Should_AcceptAnOrdinarySourceFileName(string name)
    {
        // Act
        ApiResult<string> result = WorkspaceFileName.Validate(name, []);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(name);
    }

    // Nothing that could point outside the workspace folder, or need quoting on a command line.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../main.c")]
    [InlineData("src/main.c")]
    [InlineData("src\\main.c")]
    [InlineData("my file.c")]
    [InlineData(".hidden")]
    [InlineData("main.")]
    [InlineData("main.c.tmp")]
    [InlineData("naïve.c")]
    public void Validate_Should_RefuseANameThatIsNotAPlainFileName(string name)
    {
        // Act
        ApiResult<string> result = WorkspaceFileName.Validate(name, []);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("Workspace.InvalidName");
    }

    [Theory]
    [InlineData("con")]
    [InlineData("NUL.txt")]
    [InlineData("com1.c")]
    [InlineData("LPT9")]
    public void Validate_Should_RefuseANameWindowsReserves(string name)
    {
        // Act
        ApiResult<string> result = WorkspaceFileName.Validate(name, []);

        // Assert
        result.Error!.Code.ShouldBe("Workspace.ReservedName");
    }

    // Windows ignores case in file names, so these would be one file there.
    [Fact]
    public void Validate_Should_RefuseANameThatDiffersOnlyInCase()
    {
        // Act
        ApiResult<string> result = WorkspaceFileName.Validate("MAIN.c", ["main.c"]);

        // Assert
        result.Error!.Code.ShouldBe("Workspace.NameTaken");
    }

    [Fact]
    public void Validate_Should_LetAFileBeRenamedToADifferentCaseOfItself()
    {
        // Act
        ApiResult<string> result = WorkspaceFileName.Validate("Main.c", ["main.c", "util.h"], renaming: "main.c");

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
