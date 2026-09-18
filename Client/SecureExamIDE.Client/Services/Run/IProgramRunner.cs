using System.Threading.Channels;

namespace SecureExamIDE.Client.Services.Run;

// Compiles the student's files with the exam's toolchain and runs the program, offline, on the
// student's own computer.
//
// Output arrives through the callback as it is produced, so a program that prints before it asks a
// question shows the question; input is read from the channel, which is what the console panel writes
// into. Cancelling the token stops the program and everything it started.
public interface IProgramRunner
{
    Task<RunResult> RunAsync(
        RunRequest request,
        Action<RunOutputLine> output,
        ChannelReader<string> input,
        CancellationToken cancellationToken = default);
}
