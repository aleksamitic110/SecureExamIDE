namespace SecureExamIDE.Client.Services.Storage;

internal static class ClientPaths
{
    // %LOCALAPPDATA%\SecureExamIDE on Windows, ~/.local/share/SecureExamIDE on Linux: per user,
    // per machine, and never synchronised to another computer by a roaming profile.
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "SecureExamIDE");
}
