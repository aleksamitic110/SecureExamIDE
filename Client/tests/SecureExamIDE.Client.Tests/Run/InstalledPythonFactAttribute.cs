using System.Diagnostics;

namespace SecureExamIDE.Client.Tests.Run;

// Running Python really runs it: these tests drive an interpreter on the machine that runs them, and
// are reported as skipped where there is none rather than passing without having run anything.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InstalledPythonFactAttribute : FactAttribute
{
    public InstalledPythonFactAttribute()
    {
        if (InstalledPython.Path is null)
        {
            Skip = "No Python was found on this computer; install Python 3 to run this test.";
        }
    }
}

internal static class InstalledPython
{
    // Declared first: the search below reads it, and static fields are set in the order written.
    private static readonly string[] Names = OperatingSystem.IsWindows() ? ["python.exe"] : ["python3", "python"];

    public static string? Path { get; } = Find();

    // Windows keeps a python.exe on PATH that only opens the Store, so a candidate has to answer
    // --version before it counts as an interpreter.
    private static string? Find() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => Names.Select(name => System.IO.Path.Combine(directory, name)))
            .FirstOrDefault(candidate => File.Exists(candidate) && Answers(candidate));

    private static bool Answers(string candidate)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = candidate,
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            return process.WaitForExit(TimeSpan.FromSeconds(10)) && process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
