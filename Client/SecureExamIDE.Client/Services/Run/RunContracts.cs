using SecureExamIDE.Client.Services.Toolchains;

namespace SecureExamIDE.Client.Services.Run;

// One press of Run: the student's files as they stand, the toolchain to build them with, and the
// limits that stop a runaway program.
public sealed record RunRequest(
    Toolchain Toolchain,
    string BuildDirectory,
    IReadOnlyList<SourceFile> Sources,
    TimeSpan ProcessorTimeLimit,
    long MaxOutputBytes);

// A plain copy of a workspace file. The files are encrypted at rest, so this is the only moment they
// exist as text on disk - in the build folder, which is deleted when the run ends.
public sealed record SourceFile(string Name, string Text);

public enum RunOutputKind
{
    // What the compiler said: warnings and errors.
    Compiler,

    // The program's own standard output.
    Output,

    // The program's standard error.
    Error,

    // The application speaking: what it is doing, and why a program was stopped.
    Notice
}

public sealed record RunOutputLine(string Text, RunOutputKind Kind);

public sealed record RunResult(bool Compiled, int? ExitCode, TimeSpan Duration);
