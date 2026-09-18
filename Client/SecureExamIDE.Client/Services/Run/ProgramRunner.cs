using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace SecureExamIDE.Client.Services.Run;

internal sealed class ProgramRunner(TimeProvider timeProvider) : IProgramRunner
{
    public async Task<RunResult> RunAsync(
        RunRequest request,
        Action<RunOutputLine> output,
        ChannelReader<string> input,
        CancellationToken cancellationToken = default)
    {
        long startedAt = timeProvider.GetTimestamp();

        try
        {
            WriteSources(request);

            string programPath = Path.Combine(request.BuildDirectory, OperatingSystem.IsWindows() ? "program.exe" : "program");

            if (!await CompileAsync(request, programPath, output, cancellationToken))
            {
                return new RunResult(Compiled: false, ExitCode: null, timeProvider.GetElapsedTime(startedAt));
            }

            output(new RunOutputLine("Running.", RunOutputKind.Notice));

            int? exitCode = await ExecuteAsync(request, programPath, output, input, cancellationToken);

            return new RunResult(Compiled: true, exitCode, timeProvider.GetElapsedTime(startedAt));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            output(new RunOutputLine($"The build folder could not be prepared: {exception.Message}", RunOutputKind.Notice));

            return new RunResult(Compiled: false, ExitCode: null, timeProvider.GetElapsedTime(startedAt));
        }
        finally
        {
            // The plain copies of the student's files, and the program built from them, do not outlive
            // the run: the encrypted files in the workspace are the only lasting copy.
            Clean(request.BuildDirectory);
        }
    }

    private static void WriteSources(RunRequest request)
    {
        Clean(request.BuildDirectory);
        Directory.CreateDirectory(request.BuildDirectory);

        foreach (SourceFile source in request.Sources)
        {
            File.WriteAllText(Path.Combine(request.BuildDirectory, Path.GetFileName(source.Name)), source.Text, Utf8);
        }
    }

    private async Task<bool> CompileAsync(
        RunRequest request,
        string programPath,
        Action<RunOutputLine> output,
        CancellationToken cancellationToken)
    {
        bool cpp = SourceLanguage.IsCpp(request.Sources);
        string? compiler = cpp ? request.Toolchain.CppCompilerPath : request.Toolchain.CCompilerPath;

        if (compiler is null)
        {
            output(new RunOutputLine(
                $"The exam's toolchain '{request.Toolchain.Name}' has no {(cpp ? "C++" : "C")} compiler on this computer.",
                RunOutputKind.Notice));

            return false;
        }

        string[] sources = [.. request.Sources.Where(SourceLanguage.IsCompiled).Select(source => Path.GetFileName(source.Name))];

        if (sources.Length == 0)
        {
            output(new RunOutputLine("There is nothing to compile: no .c or .cpp file in the workspace.", RunOutputKind.Notice));

            return false;
        }

        var arguments = new List<string> { cpp ? "-std=c++20" : "-std=c17", "-Wall", "-O0", "-g" };
        arguments.AddRange(sources);
        arguments.Add("-o");
        arguments.Add(programPath);

        output(new RunOutputLine($"{Path.GetFileName(compiler)} {string.Join(' ', arguments)}", RunOutputKind.Notice));

        using var process = new Process { StartInfo = StartInfo(compiler, arguments, request.BuildDirectory) };

        if (!TryStart(process, output, $"The compiler '{Path.GetFileName(compiler)}'"))
        {
            return false;
        }

        Task compilerOutput = ReadAsync(process.StandardOutput, RunOutputKind.Compiler, output, request.MaxOutputBytes, cancellationToken);
        Task compilerErrors = ReadAsync(process.StandardError, RunOutputKind.Compiler, output, request.MaxOutputBytes, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(compilerOutput, compilerErrors);

        if (process.ExitCode == 0)
        {
            return true;
        }

        output(new RunOutputLine($"Compilation failed ({process.ExitCode}).", RunOutputKind.Notice));

        return false;
    }

    private async Task<int?> ExecuteAsync(
        RunRequest request,
        string programPath,
        Action<RunOutputLine> output,
        ChannelReader<string> input,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo(programPath, [], request.BuildDirectory) };

        if (!TryStart(process, output, "The compiled program"))
        {
            return null;
        }

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task programOutput = ReadAsync(process.StandardOutput, RunOutputKind.Output, output, request.MaxOutputBytes, stopping.Token);
        Task programErrors = ReadAsync(process.StandardError, RunOutputKind.Error, output, request.MaxOutputBytes, stopping.Token);
#pragma warning disable CA2025 // Both tasks are awaited below, before the process is disposed.
        Task typing = FeedInputAsync(process, input, stopping.Token);
        Task watching = WatchAsync(process, request, output, stopping.Token);
#pragma warning restore CA2025

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The Stop button, or leaving the exam.
            output(new RunOutputLine("Stopped.", RunOutputKind.Notice));
        }

        Kill(process);
        await stopping.CancelAsync();

        // The readers end when the streams close; the watcher and the typing loop end with the token.
        await Task.WhenAll(
            Quietly(programOutput),
            Quietly(programErrors),
            Quietly(typing),
            Quietly(watching));

        return process.HasExited ? process.ExitCode : null;
    }

    // Reads character by character rather than line by line, so a prompt printed without a newline -
    // "Enter a number: " - appears before the student is expected to answer it.
    private static async Task ReadAsync(
        StreamReader reader,
        RunOutputKind kind,
        Action<RunOutputLine> output,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[1024];
        long written = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await reader.ReadAsync(buffer, cancellationToken);

                if (read == 0)
                {
                    return;
                }

                written += read;

                if (written > maxBytes)
                {
                    output(new RunOutputLine("The program printed too much; the rest is not shown.", RunOutputKind.Notice));

                    return;
                }

                output(new RunOutputLine(new string(buffer, 0, read), kind));
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The program ended, or the run was stopped.
        }
    }

    private static async Task FeedInputAsync(Process process, ChannelReader<string> input, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string line in input.ReadAllAsync(cancellationToken))
            {
                await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The program stopped reading, or ended.
        }
    }

    // An endless loop is what this catches. Processor time is watched rather than the clock, so a
    // program waiting for the student to type is never stopped for being slow.
    private async Task WatchAsync(
        Process process,
        RunRequest request,
        Action<RunOutputLine> output,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken);

                if (!process.HasExited && process.TotalProcessorTime > request.ProcessorTimeLimit)
                {
                    output(new RunOutputLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Stopped after {request.ProcessorTimeLimit.TotalSeconds:0} seconds of processor time. An endless loop, perhaps?"),
                        RunOutputKind.Notice));

                    Kill(process);

                    return;
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            // The process ended while it was being looked at.
        }
    }

    private static ProcessStartInfo StartInfo(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    // A toolchain can be unpacked and still not be runnable - the wrong platform's build, or an
    // archive that never held a compiler at all. That is said plainly instead of throwing.
    private static bool TryStart(Process process, Action<RunOutputLine> output, string what)
    {
        try
        {
            process.Start();

            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            output(new RunOutputLine($"{what} could not be started: {exception.Message}", RunOutputKind.Notice));

            return false;
        }
    }

    // The whole tree: a program that started something else must not be left behind.
    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or SystemException)
        {
            // Already gone.
        }
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected when a run is stopped.
        }
    }

    private static void Clean(string directory)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Windows keeps a program's file locked for a moment after it ends.
                Thread.Sleep(100);
            }
        }
    }

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
}
