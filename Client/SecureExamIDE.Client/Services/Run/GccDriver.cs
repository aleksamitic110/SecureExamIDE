using System.Text.RegularExpressions;
using SecureExamIDE.Client.Services.Toolchains;

namespace SecureExamIDE.Client.Services.Run;

// C and C++ with GCC, which on Windows means MinGW-w64. One driver covers both languages because one
// toolchain carries both compilers and the choice between them is made per set of files.
internal sealed partial class GccDriver : ILanguageDriver
{
    public ToolchainKind RequiredToolchain => ToolchainKind.Gcc;

    public string FileKinds => ".c or .cpp";

    public bool Claims(IReadOnlyList<SourceFile> sources) => sources.Any(IsCompiled);

    public bool CanBuildWith(Toolchain toolchain, IReadOnlyList<SourceFile> sources) =>
        toolchain.Has(CompilerFor(sources));

    public string LanguageOf(IReadOnlyList<SourceFile> sources) => IsCpp(sources) ? "C++" : "C";

    // Two programs can sit side by side in one workspace - main.c and main2.cpp, each with its own
    // main - and the one the student is looking at is the one Run means. Every other file that defines
    // its own entry point is left out, because handing the linker two of them only produces "multiple
    // definition of main". Helper files, which define none, are compiled in exactly as before, so a
    // program split across several files still builds.
    public static IReadOnlyList<SourceFile> SourcesFor(IReadOnlyList<SourceFile> sources, string? entryName)
    {
        List<SourceFile> compiled = [.. sources.Where(IsCompiled)];

        SourceFile? entry =
            compiled.Find(source => string.Equals(source.Name, entryName, StringComparison.OrdinalIgnoreCase))
            ?? compiled.Find(DefinesEntryPoint);

        // No file defines an entry point, or the one on screen is not a source file at all: compile
        // everything and let the compiler say what is wrong, which is what it is better at.
        return entry is null || !DefinesEntryPoint(entry)
            ? compiled
            : [.. compiled.Where(source => source == entry || !DefinesEntryPoint(source))];
    }

    // No -std is asked for, on purpose. A student's compiler can be any age - a 2016 MinGW rejects
    // -std=c17 outright and stops there - and the exam pins no language level, so naming a standard can
    // only turn a compiler that works into an error the student cannot do anything about. Each
    // compiler's own default is used instead: C11 on that old MinGW, C17 on a current one.
    public Invocation? Compile(BuildContext context)
    {
        IReadOnlyList<SourceFile> building = SourcesFor(context.Sources, context.EntryName);

        List<string> arguments = ["-Wall", "-O0", "-g"];

        arguments.AddRange(building.Select(source => Path.GetFileName(source.Name)));
        arguments.Add("-o");
        arguments.Add(context.ProgramPath);

        // The compiler is chosen from the files actually being built, not from everything in the
        // workspace: a C program should not be handed to g++ because an unrelated C++ file sits beside
        // it. Not null, because the runner asks CanBuildWith first.
        return new Invocation(context.Toolchain.Program(CompilerFor(building))!, arguments);
    }

    public Invocation Run(BuildContext context) => new(context.ProgramPath, []);

    // One C++ file makes it a C++ build: g++ compiles C as well, but gcc does not link a C++ program.
    private static ToolName CompilerFor(IReadOnlyList<SourceFile> sources) =>
        IsCpp(sources) ? ToolName.CppCompiler : ToolName.CCompiler;

    private static bool IsCpp(IEnumerable<SourceFile> sources) =>
        sources.Any(source => CppExtensions.Contains(Path.GetExtension(source.Name)));

    private static bool IsCompiled(SourceFile source) =>
        CppExtensions.Contains(Path.GetExtension(source.Name)) ||
        string.Equals(Path.GetExtension(source.Name), ".c", StringComparison.OrdinalIgnoreCase);

    private static bool DefinesEntryPoint(SourceFile source) => EntryPoint().IsMatch(source.Text);

    // Enough to tell two programs apart: "main(" at the start of a line, on its own or after a return
    // type. It does not have to be perfect, and it is deliberately anchored so that main inside a
    // string or behind a comment marker does not count. If it ever judges wrongly, the worst case is
    // the behaviour this replaced - a file left in or out of one build the student can still fix by
    // hand - and never a wrong answer about the student's work.
    [GeneratedRegex(@"^\s*(?:\w[\w\s\*&:<>,]*\s)?main\s*\(", RegexOptions.Multiline)]
    private static partial Regex EntryPoint();

    private static readonly HashSet<string> CppExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cpp", ".cc", ".cxx", ".c++" };
}
