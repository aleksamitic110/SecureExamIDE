namespace SecureExamIDE.Client.Tests.Run;

// Compiling really compiles: these tests drive a C compiler on the machine that runs them. Where none
// is installed they are reported as skipped rather than passing without having compiled anything.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InstalledCompilerFactAttribute : FactAttribute
{
    public InstalledCompilerFactAttribute()
    {
        if (InstalledCompiler.C is null)
        {
            Skip = "No C compiler was found on this computer; install gcc (or MinGW-w64 on Windows) to run this test.";
        }
    }
}

internal static class InstalledCompiler
{
    public static string? C => Find(OperatingSystem.IsWindows() ? "gcc.exe" : "gcc");

    public static string? Cpp => Find(OperatingSystem.IsWindows() ? "g++.exe" : "g++");

    private static string? Find(string fileName) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
}
