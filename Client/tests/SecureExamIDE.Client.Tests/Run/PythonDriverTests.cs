using SecureExamIDE.Client.Services.Run;
using SecureExamIDE.Client.Services.Toolchains;

namespace SecureExamIDE.Client.Tests.Run;

public sealed class PythonDriverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PythonDriver _driver = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Toolchain Python(string path = "/tools/python/python") => new(
        ToolchainKind.Python, "Python", "3.13", "/tools/python", new Dictionary<ToolName, string> { [ToolName.PythonRuntime] = path });

    private static BuildContext Context(string? entry, params SourceFile[] sources) =>
        new(Python(), "/build", sources, "/build/program", entry);

    private static SourceFile Script(string name) => new(name, "print('hi')");

    [Fact]
    public void Claims_Should_OwnPythonFiles_AndNothingElse()
    {
        // Assert
        _driver.Claims([Script("main.py")]).ShouldBeTrue();
        _driver.Claims([Script("MAIN.PY")]).ShouldBeTrue();
        _driver.Claims([new SourceFile("main.c", ""), new SourceFile("notes.txt", "")]).ShouldBeFalse();
    }

    [Fact]
    public void CanBuildWith_Should_NeedAnInterpreter()
    {
        // Arrange
        var gcc = new Toolchain(
            ToolchainKind.Gcc, "GCC", "14.2.0", "/tools", new Dictionary<ToolName, string> { [ToolName.CCompiler] = "/tools/bin/gcc" });

        // Assert
        _driver.CanBuildWith(Python(), [Script("main.py")]).ShouldBeTrue();
        _driver.CanBuildWith(gcc, [Script("main.py")]).ShouldBeFalse();
    }

    [Fact]
    public void Compile_Should_BeNothingAtAll()
    {
        // Assert
        _driver.Compile(Context("main.py", Script("main.py"))).ShouldBeNull();
        _driver.IsCompiled.ShouldBeFalse();
    }

    // Unbuffered, or the prompt of an input() arrives after the answer it was asking for.
    [Fact]
    public void Run_Should_StartTheFileOnScreen_Unbuffered()
    {
        // Act
        Invocation run = _driver.Run(Context("second.py", Script("main.py"), Script("second.py")));

        // Assert
        run.FileName.ShouldBe("/tools/python/python");
        run.Arguments.ShouldBe(["-u", "-X", "utf8", "second.py"]);
    }

    [Fact]
    public void Run_Should_StartMainPy_WhenTheFileOnScreenIsNotPython()
    {
        // Act
        Invocation run = _driver.Run(Context("notes.txt", Script("alpha.py"), Script("main.py"), new SourceFile("notes.txt", "")));

        // Assert
        run.Arguments[^1].ShouldBe("main.py");
    }

    [Fact]
    public void Run_Should_StartTheOnlyScript_WhenThereIsNoMainPy()
    {
        // Act
        Invocation run = _driver.Run(Context(null, Script("solution.py")));

        // Assert
        run.Arguments[^1].ShouldBe("solution.py");
    }

    // The embeddable Windows build leaves the script's folder off the module path, so the student's
    // own helper.py could not be imported; the folder is put there before the file is run.
    [Fact]
    public void Run_Should_AddTheWorkingFolderToTheModulePath_ForAnIsolatedBuild()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "python313._pth"), "python313.zip\n.\n");
        string python = Path.Combine(_directory, "python.exe");

        var context = new BuildContext(Python(python), "/build", [Script("main.py")], "/build/program", "main.py");

        // Act
        Invocation run = _driver.Run(context);

        // Assert
        run.Arguments.Take(4).ShouldBe(["-u", "-X", "utf8", "-c"]);
        run.Arguments[4].ShouldContain("sys.path.insert(0, os.getcwd())");
        run.Arguments[^1].ShouldBe("main.py");
    }
}
