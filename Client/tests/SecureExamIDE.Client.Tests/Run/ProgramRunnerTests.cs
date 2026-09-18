using System.Collections.Concurrent;
using System.Threading.Channels;
using SecureExamIDE.Client.Services.Run;
using SecureExamIDE.Client.Services.Toolchains;

namespace SecureExamIDE.Client.Tests.Run;

public sealed class ProgramRunnerTests : IDisposable
{
    private readonly string _buildDirectory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<RunOutputLine> _output = new();
    private readonly ProgramRunner _runner = new(TimeProvider.System);

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
        new(ToolchainKind.Gcc, "GCC", "local", "/", InstalledCompiler.C, InstalledCompiler.Cpp);

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
        var toolchain = new Toolchain(ToolchainKind.Gcc, "GCC", "14.2.0", "/tools", null, null);
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

        var toolchain = new Toolchain(ToolchainKind.Gcc, "GCC", "14.2.0", _buildDirectory, notACompiler, notACompiler);
        var request = new RunRequest(
            toolchain, _buildDirectory, [new SourceFile("main.c", "int main(void){return 0;}")], TimeSpan.FromSeconds(5), 1000);

        // Act
        RunResult result = await RunAsync(request);

        // Assert
        result.Compiled.ShouldBeFalse();
        Text(RunOutputKind.Notice).ShouldContain("could not be started");
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
