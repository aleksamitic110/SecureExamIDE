namespace SecureExamIDE.Client.Services.Toolchains;

// What the client understands about a downloaded archive. The name the professor gave the dependency
// is what says which kind it is - there is no manifest, by decision - and everything else is found by
// looking inside the unpacked folder.
public enum ToolchainKind
{
    // A library or a set of headers: unpacked like anything else, but nothing in it compiles.
    Unknown,

    Gcc,

    Jdk,

    Python,

    DotnetSdk
}

// One program inside a toolchain, asked for by name rather than by a field of its own. A GCC build
// carries two compilers, a JDK a compiler and a runtime, a .NET SDK one program that does both - so
// which programs a toolchain holds is data, and a new kind needs no new field here.
public enum ToolName
{
    CCompiler,

    CppCompiler,

    JavaCompiler,

    JavaRuntime,

    PythonRuntime,

    DotnetSdk
}

// A toolchain unpacked on this computer and ready to use.
public sealed record Toolchain(
    ToolchainKind Kind,
    string Name,
    string Version,
    string RootDirectory,
    // Only the programs actually found and proven to start. A kind the client recognises but whose
    // archive turned out to hold nothing runnable has an empty map, so it is never chosen to build with.
    IReadOnlyDictionary<ToolName, string> Programs,
    // True for a compiler found on this computer rather than downloaded with the exam. The console
    // says so, because it is not the compiler the professor chose.
    bool IsFromThisComputer = false)
{
    public string? Program(ToolName tool) => Programs.GetValueOrDefault(tool);

    public bool Has(ToolName tool) => Programs.ContainsKey(tool);
}
