namespace SecureExamIDE.Client.Services.Toolchains;

// What the client understands about a downloaded archive. The name the professor gave the dependency
// is what says which kind it is - there is no manifest, by decision - and everything else is found by
// looking inside the unpacked folder.
public enum ToolchainKind
{
    Unknown,
    Gcc
}

// A toolchain unpacked on this computer and ready to use.
public sealed record Toolchain(
    ToolchainKind Kind,
    string Name,
    string Version,
    string RootDirectory,
    string? CCompilerPath,
    string? CppCompilerPath,
    // True for a compiler found on this computer rather than downloaded with the exam. The console
    // says so, because it is not the compiler the professor chose.
    bool IsFromThisComputer = false)
{
    public bool CanCompileC => CCompilerPath is not null;

    public bool CanCompileCpp => CppCompilerPath is not null;
}
