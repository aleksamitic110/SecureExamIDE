namespace SecureExamIDE.Client.Services.Run;

// Which compiler a set of files needs. There is no project file and no setting to get wrong: one C++
// file makes it a C++ build, because g++ compiles C as well but gcc does not link a C++ program.
internal static class SourceLanguage
{
    public static bool IsCpp(IEnumerable<SourceFile> sources) =>
        sources.Any(source => CppExtensions.Contains(Path.GetExtension(source.Name)));

    public static bool IsCompiled(SourceFile source) =>
        CppExtensions.Contains(Path.GetExtension(source.Name)) ||
        string.Equals(Path.GetExtension(source.Name), ".c", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> CppExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cpp", ".cc", ".cxx", ".c++" };
}
