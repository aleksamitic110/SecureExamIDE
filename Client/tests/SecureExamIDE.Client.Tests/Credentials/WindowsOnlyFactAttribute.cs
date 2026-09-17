namespace SecureExamIDE.Client.Tests.Credentials;

// A test that can only run on Windows is reported as skipped elsewhere, rather than passing without
// having tested anything.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Uses a Windows-only API; run it on the Windows machine.";
        }
    }
}
