using SecureExamIDE.Client.Services.Run;

namespace SecureExamIDE.Client.Tests.Run;

public sealed class LanguageDriversTests
{
    private readonly LanguageDrivers _drivers = new([new GccDriver(), new PythonDriver()]);

    private static readonly SourceFile[] Both = [new("main.c", "int main(void){return 0;}"), new("main.py", "print(1)")];

    // Two languages side by side in one workspace: Run means the one the student is looking at.
    [Fact]
    public void ForSources_Should_ChooseTheLanguageOfTheFileOnScreen()
    {
        // Assert
        _drivers.ForSources(Both, "main.py").ShouldBeOfType<PythonDriver>();
        _drivers.ForSources(Both, "main.c").ShouldBeOfType<GccDriver>();
    }

    [Fact]
    public void ForSources_Should_FallBackToAnyLanguageInTheWorkspace_WhenTheFileOnScreenIsNotSource()
    {
        // Arrange
        SourceFile[] sources = [new("notes.txt", ""), new("main.py", "print(1)")];

        // Assert
        _drivers.ForSources(sources, "notes.txt").ShouldBeOfType<PythonDriver>();
        _drivers.ForSources(sources).ShouldBeOfType<PythonDriver>();
    }

    [Fact]
    public void ForSources_Should_FindNothing_WhenNoFileCanBeRun()
    {
        // Assert
        _drivers.ForSources([new SourceFile("notes.txt", "")], "notes.txt").ShouldBeNull();
    }

    [Fact]
    public void FileKinds_Should_NameEveryKindOfFile()
    {
        // Assert
        _drivers.FileKinds.ShouldBe(".c, .cpp, .py");
    }
}
