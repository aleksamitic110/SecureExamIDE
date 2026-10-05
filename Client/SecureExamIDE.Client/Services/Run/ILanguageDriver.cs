using SecureExamIDE.Client.Services.Toolchains;

namespace SecureExamIDE.Client.Services.Run;

// One program to start: the file to run and the arguments to give it. Arguments are kept as a list so
// they reach ProcessStartInfo.ArgumentList and never need quoting.
public sealed record Invocation(string FileName, IReadOnlyList<string> Arguments);

// What a driver is given to build with: the student's files, where the plain copies were written, and
// the name the built program should have.
public sealed record BuildContext(
    Toolchain Toolchain,
    string BuildDirectory,
    IReadOnlyList<SourceFile> Sources,
    string ProgramPath,
    string? EntryName = null);

// How one language is built and run. The runner owns the process handling, the limits and the console;
// a driver owns only the knowledge of a language - which files are its own, which programs it needs out
// of a toolchain, and what to type on the command line.
//
// Adding a language is therefore a driver plus a row in the toolchain's name table, and nothing in the
// runner changes. Java would compile with javac and run "java Main"; Python would return no compile
// step at all and run the script directly.
public interface ILanguageDriver
{
    // The kind of toolchain this driver can be given. A toolchain of another kind is never offered.
    ToolchainKind RequiredToolchain { get; }

    // Named in the message when the workspace holds nothing this driver can build.
    string FileKinds { get; }

    // Are these the driver's files at all?
    bool Claims(IReadOnlyList<SourceFile> sources);

    // Does this toolchain hold the programs these files need? It depends on the files, not only on the
    // toolchain: a C++ file needs g++ where a C file needs only gcc.
    bool CanBuildWith(Toolchain toolchain, IReadOnlyList<SourceFile> sources);

    // What to call the language in the console, for these files.
    string LanguageOf(IReadOnlyList<SourceFile> sources);

    // Null when the language needs no compile step, which is what an interpreted language wants.
    Invocation? Compile(BuildContext context);

    Invocation Run(BuildContext context);
}

// The drivers this version knows, asked which one owns the files on screen.
public interface ILanguageDrivers
{
    ILanguageDriver? ForSources(IReadOnlyList<SourceFile> sources);

    // Every kind of file that can be built, for the "nothing to compile" message - so that message
    // stays true by itself as drivers are added.
    string FileKinds { get; }
}
