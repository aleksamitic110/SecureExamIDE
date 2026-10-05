using SecureExamIDE.Client.Services.Run;
using SecureExamIDE.Client.Services.Toolchains;

namespace SecureExamIDE.Client.Tests.Run;

public sealed class GccDriverTests
{
    private readonly GccDriver _driver = new();

    private static Toolchain Both() => new(
        ToolchainKind.Gcc,
        "GCC",
        "14.2.0",
        "/tools",
        new Dictionary<ToolName, string>
        {
            [ToolName.CCompiler] = "/tools/bin/gcc",
            [ToolName.CppCompiler] = "/tools/bin/g++"
        });

    private static Toolchain COnly() => new(
        ToolchainKind.Gcc, "GCC", "14.2.0", "/tools", new Dictionary<ToolName, string> { [ToolName.CCompiler] = "/tools/bin/gcc" });

    private static BuildContext Context(Toolchain toolchain, params SourceFile[] sources) =>
        new(toolchain, "/build", sources, "/build/program.exe");

    private static BuildContext Entering(string entry, params SourceFile[] sources) =>
        new(Both(), "/build", sources, "/build/program.exe", entry);

    private static SourceFile Program(string name, string greeting = "hi") => new(
        name,
        $$"""
          #include <stdio.h>
          int main(void)
          {
              printf("{{greeting}}");
              return 0;
          }
          """);

    private static SourceFile Helper(string name) => new(
        name,
        """
        int helper(int value)
        {
            return value + 1;
        }
        """);

    [Fact]
    public void Claims_Should_RecogniseSourceFiles_AndNothingElse()
    {
        // Assert
        _driver.Claims([new SourceFile("main.c", "")]).ShouldBeTrue();
        _driver.Claims([new SourceFile("main.CPP", "")]).ShouldBeTrue();
        _driver.Claims([new SourceFile("notes.txt", "")]).ShouldBeFalse();
        _driver.Claims([new SourceFile("Main.java", "")]).ShouldBeFalse();
    }

    // No -std is asked for: a compiler old enough to reject the one named would otherwise refuse to
    // build a program it is perfectly capable of building. This is the regression that cost an exam.
    [Fact]
    public void Compile_Should_NotNameALanguageStandard()
    {
        // Act
        Invocation? compile = _driver.Compile(Context(Both(), new SourceFile("main.c", "")));

        // Assert
        compile.ShouldNotBeNull();
        compile.Arguments.ShouldNotContain(argument => argument.StartsWith("-std", StringComparison.Ordinal));
        compile.Arguments.ShouldContain("-Wall");
        compile.Arguments.ShouldContain("-o");
        compile.Arguments[^1].ShouldBe("/build/program.exe");
    }

    // One C++ file makes it a C++ build: g++ compiles C too, but gcc does not link a C++ program.
    [Fact]
    public void Compile_Should_UseTheCppCompiler_WhenAnyFileIsCpp()
    {
        // Act
        Invocation? compile = _driver.Compile(
            Context(Both(), new SourceFile("helper.c", ""), new SourceFile("main.cpp", "")));

        // Assert
        compile.ShouldNotBeNull();
        compile.FileName.ShouldBe("/tools/bin/g++");
        compile.Arguments.ShouldContain("helper.c");
        compile.Arguments.ShouldContain("main.cpp");
    }

    [Fact]
    public void Compile_Should_UseTheCCompiler_ForCFilesAlone()
    {
        // Act
        Invocation? compile = _driver.Compile(Context(Both(), new SourceFile("main.c", "")));

        // Assert
        compile.ShouldNotBeNull();
        compile.FileName.ShouldBe("/tools/bin/gcc");
    }

    // Only the files that are compiled reach the command line; the rest are the student's own notes.
    [Fact]
    public void Compile_Should_PassOnlySourceFiles()
    {
        // Act
        Invocation? compile = _driver.Compile(
            Context(Both(), new SourceFile("main.c", ""), new SourceFile("notes.txt", "")));

        // Assert
        compile.ShouldNotBeNull();
        compile.Arguments.ShouldNotContain("notes.txt");
    }

    // A toolchain with gcc but no g++ can build C and cannot build C++, which is what the console has
    // to be able to say rather than failing in the linker.
    [Fact]
    public void CanBuildWith_Should_DependOnTheFiles_NotOnlyTheToolchain()
    {
        // Assert
        _driver.CanBuildWith(COnly(), [new SourceFile("main.c", "")]).ShouldBeTrue();
        _driver.CanBuildWith(COnly(), [new SourceFile("main.cpp", "")]).ShouldBeFalse();
        _driver.CanBuildWith(Both(), [new SourceFile("main.cpp", "")]).ShouldBeTrue();
    }

    [Fact]
    public void LanguageOf_Should_NameTheLanguage_ForTheConsole()
    {
        // Assert
        _driver.LanguageOf([new SourceFile("main.c", "")]).ShouldBe("C");
        _driver.LanguageOf([new SourceFile("main.cpp", "")]).ShouldBe("C++");
    }

    // Two programs in one workspace, each with its own main: handing both to the linker gives
    // "multiple definition of main", so only the one the student is looking at is built.
    [Fact]
    public void Compile_Should_BuildOnlyTheFileOnScreen_WhenAnotherAlsoDefinesMain()
    {
        // Act
        Invocation? compile = _driver.Compile(Entering("main2.cpp", Program("main.c"), Program("main2.cpp")));

        // Assert
        compile.ShouldNotBeNull();
        compile.Arguments.ShouldContain("main2.cpp");
        compile.Arguments.ShouldNotContain("main.c");
    }

    [Fact]
    public void Compile_Should_BuildTheOtherOne_WhenThatIsTheFileOnScreen()
    {
        // Act
        Invocation? compile = _driver.Compile(Entering("main.c", Program("main.c"), Program("main2.cpp")));

        // Assert
        compile.ShouldNotBeNull();
        compile.Arguments.ShouldContain("main.c");
        compile.Arguments.ShouldNotContain("main2.cpp");

        // And a C program is built with gcc even though an unrelated C++ file sits beside it.
        compile.FileName.ShouldBe("/tools/bin/gcc");
    }

    // A program split across files still builds: a helper defines no main, so it is not a rival.
    [Fact]
    public void Compile_Should_KeepHelperFiles_ThatDefineNoMain()
    {
        // Act
        Invocation? compile = _driver.Compile(Entering("main.c", Program("main.c"), Helper("helper.c")));

        // Assert
        compile.ShouldNotBeNull();
        compile.Arguments.ShouldContain("main.c");
        compile.Arguments.ShouldContain("helper.c");
    }

    // Nothing is on screen, or a task file is: the first program found is built rather than nothing.
    [Fact]
    public void Compile_Should_FallBackToAFileThatDefinesMain_WhenTheScreenSaysNothingUseful()
    {
        // Act
        Invocation? compile = _driver.Compile(Entering("tasks.txt", Helper("helper.c"), Program("main.c")));

        // Assert
        compile.ShouldNotBeNull();
        compile.Arguments.ShouldContain("main.c");
        compile.Arguments.ShouldContain("helper.c");
    }

    // main behind a comment or inside a string is not an entry point, or a file would be dropped from
    // a build for mentioning it.
    [Fact]
    public void SourcesFor_Should_NotCountMainInACommentOrAString()
    {
        // Arrange
        var mentions = new SourceFile(
            "notes.c",
            """
            // int main(void) {}
            const char *s = "main(";
            """);

        // Act
        IReadOnlyList<SourceFile> building = GccDriver.SourcesFor([Program("main.c"), mentions], "main.c");

        // Assert
        building.Select(source => source.Name).ShouldBe(["main.c", "notes.c"], ignoreOrder: true);
    }

    [Fact]
    public void Run_Should_StartTheProgramItBuilt()
    {
        // Act
        Invocation run = _driver.Run(Context(Both(), new SourceFile("main.c", "")));

        // Assert
        run.FileName.ShouldBe("/build/program.exe");
        run.Arguments.ShouldBeEmpty();
    }
}
