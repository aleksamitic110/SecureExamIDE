using SecureExamIDE.Client.Services.Toolchains;

namespace SecureExamIDE.Client.Services.Run;

// Python: nothing to compile, the interpreter is simply given the file on screen.
internal sealed class PythonDriver : ILanguageDriver
{
    public ToolchainKind RequiredToolchain => ToolchainKind.Python;

    public string FileKinds => ".py";

    public bool IsCompiled => false;

    public bool Claims(IReadOnlyList<SourceFile> sources) => sources.Any(IsPython);

    public bool CanBuildWith(Toolchain toolchain, IReadOnlyList<SourceFile> sources) =>
        toolchain.Has(ToolName.PythonRuntime);

    public string LanguageOf(IReadOnlyList<SourceFile> sources) => "Python";

    public Invocation? Compile(BuildContext context) => null;

    // -u, because the console is a pipe and Python buffers a pipe: without it the prompt of an input()
    // would not appear until after the student had answered it. -X utf8, so what the program prints is
    // read back the way the console expects whatever the computer's own code page is.
    public Invocation Run(BuildContext context)
    {
        // Not null, because the runner asks CanBuildWith first.
        string python = context.Toolchain.Program(ToolName.PythonRuntime)!;
        string entry = EntryFor(context.Sources, context.EntryName);

        List<string> arguments = ["-u", "-X", "utf8"];

        if (IsIsolated(python))
        {
            // The embeddable build for Windows - the one small enough to ship with an exam - runs
            // isolated: it does not put the script's folder on the module path, so "import helper"
            // would not find the student's own helper.py beside it. The folder is added by hand and
            // the file is then run as "python file.py" would run it. The hook drops this line's own
            // frame from a traceback, so an error reads exactly as it would have: the student's file
            // and line, and nothing of the application's. Checked against python-3.13-embed-amd64.
            arguments.Add("-c");
            arguments.Add(IsolatedStart);
        }

        arguments.Add(entry);

        return new Invocation(python, arguments);
    }

    // The file on screen is the program Run means. Looking at something that is not Python - the
    // notes, say - runs main.py, and failing that the first Python file there is.
    public static string EntryFor(IReadOnlyList<SourceFile> sources, string? entryName)
    {
        List<SourceFile> scripts = [.. sources.Where(IsPython)];

        SourceFile entry =
            scripts.Find(source => string.Equals(source.Name, entryName, StringComparison.OrdinalIgnoreCase))
            ?? scripts.Find(source => string.Equals(source.Name, "main.py", StringComparison.OrdinalIgnoreCase))
            ?? scripts[0];

        return Path.GetFileName(entry.Name);
    }

    // A python312._pth file beside the interpreter is what switches the isolated mode on.
    private static bool IsIsolated(string python) =>
        Path.GetDirectoryName(python) is { Length: > 0 } folder &&
        Directory.Exists(folder) &&
        Directory.EnumerateFiles(folder, "python*._pth").Any();

    private static bool IsPython(SourceFile source) =>
        string.Equals(Path.GetExtension(source.Name), ".py", StringComparison.OrdinalIgnoreCase);

    private const string IsolatedStart =
        "import os, sys, traceback; " +
        "sys.path.insert(0, os.getcwd()); " +
        "sys.argv = sys.argv[1:]; " +
        "sys.excepthook = lambda kind, error, trace: traceback.print_exception(kind, error, trace.tb_next if trace else None); " +
        "exec(compile(open(sys.argv[0], encoding='utf-8').read(), sys.argv[0], 'exec'), {'__name__': '__main__', '__file__': sys.argv[0]})";
}
