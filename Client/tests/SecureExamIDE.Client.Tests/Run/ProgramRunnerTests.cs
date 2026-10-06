using System.Collections.Concurrent;
using System.Threading.Channels;
using SecureExamIDE.Client.Services.Run;
using SecureExamIDE.Client.Services.Toolchains;

namespace SecureExamIDE.Client.Tests.Run;

public sealed class ProgramRunnerTests : IDisposable
{
    private readonly string _buildDirectory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<RunOutputLine> _output = new();
    private readonly ProgramRunner _runner = new(TimeProvider.System, new LanguageDrivers([new GccDriver(), new PythonDriver()]));

    public void Dispose()
    {
        if (Directory.Exists(_buildDirectory))
        {
            Directory.Delete(_buildDirectory, recursive: true);
        }
    }

    private string Text(params RunOutputKind[] kinds) =>
        string.Concat(_output.Where(line => kinds.Contains(line.Kind)).Select(line => line.Text));

    private static Toolchain Installed() =>
        Gcc("GCC", "local", "/", InstalledCompiler.C, InstalledCompiler.Cpp);

    private static Toolchain Gcc(string name, string version, string root, string? c, string? cpp)
    {
        Dictionary<ToolName, string> programs = [];

        if (c is not null)
        {
            programs[ToolName.CCompiler] = c;
        }

        if (cpp is not null)
        {
            programs[ToolName.CppCompiler] = cpp;
        }

        return new Toolchain(ToolchainKind.Gcc, name, version, root, programs);
    }

    private RunRequest RequestFor(IReadOnlyList<SourceFile> sources, TimeSpan? processorLimit = null) => new(
        Installed(),
        _buildDirectory,
        sources,
        processorLimit ?? TimeSpan.FromSeconds(10),
        MaxOutputBytes: 1_000_000);

    private Task<RunResult> RunAsync(RunRequest request, ChannelReader<string>? input = null, CancellationToken cancellationToken = default) =>
        _runner.RunAsync(request, _output.Enqueue, input ?? Channel.CreateUnbounded<string>().Reader, cancellationToken);

    [InstalledCompilerFact]
    public async Task Run_Should_CompileAndRunAProgram_AndLeaveNothingBehind()
    {
        // Arrange
        var request = RequestFor([new SourceFile("main.c", """
            #include <stdio.h>
            int main(void) { printf("Hello from the exam\n"); return 0; }
            """)]);

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.Compiled.ShouldBeTrue();
        result.ExitCode.ShouldBe(0);
        Text(RunOutputKind.Output).ShouldContain("Hello from the exam");

        // The plain copies and the built program are gone; only the encrypted workspace remains.
        Directory.Exists(_buildDirectory).ShouldBeFalse();
    }

    // One C++ file is enough to make it a C++ build.
    [InstalledCompilerFact]
    public async Task Run_Should_UseTheCppCompiler_WhenAnyFileIsCpp()
    {
        // Arrange
        var request = RequestFor([new SourceFile("main.cpp", """
            #include <iostream>
            int main() { std::cout << "C++ works" << std::endl; }
            """)]);

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.Compiled.ShouldBeTrue();
        Text(RunOutputKind.Output).ShouldContain("C++ works");
    }

    [InstalledCompilerFact]
    public async Task Run_Should_ShowTheCompilersErrors_AndNotRunAnything()
    {
        // Arrange
        var request = RequestFor([new SourceFile("main.c", "int main(void) { this is not C }")]);

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.Compiled.ShouldBeFalse();
        result.ExitCode.ShouldBeNull();
        Text(RunOutputKind.Compiler).ShouldContain("error");
        Text(RunOutputKind.Output).ShouldBeEmpty();
    }

    // A program that asks a question gets an answer, and the question appears before it is answered.
    [InstalledCompilerFact]
    public async Task Run_Should_PassWhatTheStudentTypesToTheProgram()
    {
        // Arrange
        Channel<string> input = Channel.CreateUnbounded<string>();
        await input.Writer.WriteAsync("7");

        var request = RequestFor([new SourceFile("main.c", """
            #include <stdio.h>
            int main(void) {
                int value = 0;
                printf("Enter a number: ");
                fflush(stdout);
                scanf("%d", &value);
                printf("twice is %d\n", value * 2);
                return 0;
            }
            """)]);

        // Act
        RunResult result = await RunAsync(request, input.Reader);

        // Assert
        result.ExitCode.ShouldBe(0);
        Text(RunOutputKind.Output).ShouldContain("Enter a number:");
        Text(RunOutputKind.Output).ShouldContain("twice is 14");
    }

    // An endless loop is stopped by the processor-time limit rather than running until the exam ends.
    [InstalledCompilerFact]
    public async Task Run_Should_StopAProgramThatNeverFinishes()
    {
        // Arrange
        var request = RequestFor(
            [new SourceFile("main.c", "int main(void) { while (1) { } }")],
            processorLimit: TimeSpan.FromSeconds(1));

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.Compiled.ShouldBeTrue();
        Text(RunOutputKind.Notice).ShouldContain("seconds of processor time");
    }

    [InstalledCompilerFact]
    public async Task Run_Should_StopWhenTheStudentPressesStop()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        var request = RequestFor([new SourceFile("main.c", """
            #include <stdio.h>
            #include <unistd.h>
            int main(void) { printf("started\n"); fflush(stdout); sleep(60); return 0; }
            """)]);

        // Act
        Task<RunResult> running = RunAsync(request, cancellationToken: stop.Token);

        while (!Text(RunOutputKind.Output).Contains("started", StringComparison.Ordinal))
        {
            await Task.Delay(50, CancellationToken.None);
        }

        await stop.CancelAsync();
        RunResult result = await running;

        // Assert
        result.Compiled.ShouldBeTrue();
        Text(RunOutputKind.Notice).ShouldContain("Stopped.");
        Directory.Exists(_buildDirectory).ShouldBeFalse();
    }

    // No compiler at all: said plainly, rather than a failure that looks like the student's mistake.
    [Fact]
    public async Task Run_Should_SayWhenTheExamsToolchainHasNoCompiler()
    {
        // Arrange
        Toolchain toolchain = Gcc("GCC", "14.2.0", "/tools", c: null, cpp: null);
        var request = new RunRequest(
            toolchain, _buildDirectory, [new SourceFile("main.c", "int main(void){return 0;}")], TimeSpan.FromSeconds(5), 1000);

        // Act
        RunResult result = await _runner.RunAsync(request, _output.Enqueue, Channel.CreateUnbounded<string>().Reader, CancellationToken.None);

        // Assert
        result.Compiled.ShouldBeFalse();
        Text(RunOutputKind.Notice).ShouldContain("has no C compiler");
    }

    // The demo toolchains are stand-ins, and a real archive can be for the wrong platform: either way
    // the student is told, rather than seeing the application fall over.
    [Fact]
    public async Task Run_Should_SayWhenTheCompilerCannotBeStarted()
    {
        // Arrange
        Directory.CreateDirectory(_buildDirectory);
        string notACompiler = Path.Combine(_buildDirectory, "gcc-that-is-not-a-compiler");
        await File.WriteAllTextAsync(notACompiler, "This is a text file pretending to be a compiler.", CancellationToken.None);

        Toolchain toolchain = Gcc("GCC", "14.2.0", _buildDirectory, notACompiler, notACompiler);
        var request = new RunRequest(
            toolchain, _buildDirectory, [new SourceFile("main.c", "int main(void){return 0;}")], TimeSpan.FromSeconds(5), 1000);

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.Compiled.ShouldBeFalse();
        Text(RunOutputKind.Notice).ShouldContain("could not be started");
    }

    // The command is echoed into the console, so the student can see what was run - and so a standard
    // creeping back into it would be caught here rather than on a laptop in an exam room.
    [InstalledCompilerFact]
    public async Task Run_Should_EchoACommandWithoutALanguageStandard()
    {
        // Arrange
        var request = RequestFor([new SourceFile("main.c", "int main(void){return 0;}")]);

        // Act
        await RunAsync(request);

        // Assert
        string notices = Text(RunOutputKind.Notice);
        notices.ShouldContain("-Wall");
        notices.ShouldNotContain("-std");
    }

    // What a real MinGW needs: a program built by its g++ loads libstdc++ from the compiler's own bin
    // folder, so that folder has to be on PATH for the program as well as for the compiler.
    [InstalledCompilerFact]
    public async Task Run_Should_PutTheToolchainsFolderOnThePath_OfTheProgramItRuns()
    {
        // Arrange
        var request = RequestFor([new SourceFile("main.c", """
            #include <stdio.h>
            #include <stdlib.h>
            int main(void) { printf("%s", getenv("PATH")); return 0; }
            """)]);

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.ExitCode.ShouldBe(0);
        Text(RunOutputKind.Output).ShouldStartWith(Path.GetDirectoryName(InstalledCompiler.C)!, Case.Insensitive);
    }

    // Course headers the professor attached: downloaded, unpacked, and found by #include.
    [InstalledCompilerFact]
    public async Task Run_Should_FindAHeaderTheExamShipped()
    {
        // Arrange
        string library = Path.Combine(_buildDirectory + "-library", "coursekit");
        Directory.CreateDirectory(Path.Combine(library, "include"));
        await File.WriteAllTextAsync(Path.Combine(library, "include", "course.h"), "#define COURSE_ANSWER 42\n", CancellationToken.None);

        try
        {
            var request = new RunRequest(
                Installed(),
                _buildDirectory,
                [new SourceFile("main.c", """
                    #include <stdio.h>
                    #include "course.h"
                    int main(void) { printf("%d", COURSE_ANSWER); return 0; }
                    """)],
                TimeSpan.FromSeconds(10),
                1_000_000,
                Libraries: [new Toolchain(ToolchainKind.Unknown, "Course headers", "1", Path.GetDirectoryName(library)!, new Dictionary<ToolName, string>())]);

            // Act
            RunResult result = await RunAsync(request);

            // Assert
            result.Compiled.ShouldBeTrue();
            Text(RunOutputKind.Output).ShouldBe("42");
        }
        finally
        {
            Directory.Delete(_buildDirectory + "-library", recursive: true);
        }
    }

    private RunRequest PythonRequest(string? entry, params SourceFile[] sources) => new(
        new Toolchain(
            ToolchainKind.Python,
            "Python",
            "local",
            Path.GetDirectoryName(InstalledPython.Path)!,
            new Dictionary<ToolName, string> { [ToolName.PythonRuntime] = InstalledPython.Path! }),
        _buildDirectory,
        sources,
        TimeSpan.FromSeconds(10),
        1_000_000,
        entry);

    // The prompt has to be on screen before the answer is typed, which is what -u is for.
    [InstalledPythonFact]
    public async Task Run_Should_RunAPythonProgram_AndShowItsPromptBeforeTheAnswer()
    {
        // Arrange
        RunRequest request = PythonRequest("main.py", new SourceFile("main.py", """
            name = input("Your name: ")
            print("Hello, " + name)
            """));

        Channel<string> input = Channel.CreateUnbounded<string>();

        // Act
        Task<RunResult> running = RunAsync(request, input.Reader);

        for (int attempt = 0; attempt < 200 && !Text(RunOutputKind.Output).Contains("Your name: ", StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(50);
        }

        string beforeAnswering = Text(RunOutputKind.Output);
        input.Writer.TryWrite("Aleksa");
        RunResult result = await running;

        // Assert
        beforeAnswering.ShouldBe("Your name: ");
        result.Compiled.ShouldBeTrue();
        result.ExitCode.ShouldBe(0);
        Text(RunOutputKind.Output).ShouldContain("Hello, Aleksa");
        Directory.Exists(_buildDirectory).ShouldBeFalse();
    }

    [InstalledPythonFact]
    public async Task Run_Should_LetAPythonProgramImportTheStudentsOtherFile()
    {
        // Arrange
        RunRequest request = PythonRequest(
            "main.py",
            new SourceFile("main.py", "import helper\nprint(helper.answer())\n"),
            new SourceFile("helper.py", "def answer():\n    return 42\n"));

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.ExitCode.ShouldBe(0);
        Text(RunOutputKind.Output).Trim().ShouldBe("42");
    }

    // A C file on screen beside a Python one: the file being looked at is the program Run means.
    [InstalledPythonFact]
    public async Task Run_Should_RunTheLanguageOfTheFileOnScreen()
    {
        // Arrange
        RunRequest request = PythonRequest(
            "hello.py",
            new SourceFile("main.c", "int main(void){return 0;}"),
            new SourceFile("hello.py", "print('from python')\n"));

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.ExitCode.ShouldBe(0);
        Text(RunOutputKind.Output).ShouldContain("from python");
    }

    [Fact]
    public async Task Run_Should_SayWhenTheExamsToolchainHasNoPython()
    {
        // Arrange
        Toolchain toolchain = Gcc("GCC", "14.2.0", "/tools", c: null, cpp: null);
        var request = new RunRequest(
            toolchain, _buildDirectory, [new SourceFile("main.py", "print(1)")], TimeSpan.FromSeconds(5), 1000);

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.Compiled.ShouldBeFalse();
        Text(RunOutputKind.Notice).ShouldContain("has no Python interpreter");
    }

    [Fact]
    public async Task Run_Should_SayWhenThereIsNothingToCompile()
    {
        // Arrange
        var request = RequestFor([new SourceFile("notes.txt", "just some notes")]);

        // Act
        RunResult result = await _runner.RunAsync(request, _output.Enqueue, Channel.CreateUnbounded<string>().Reader, CancellationToken.None);

        // Assert
        result.Compiled.ShouldBeFalse();
        Text(RunOutputKind.Notice).ShouldContain("nothing to compile");
    }
}
