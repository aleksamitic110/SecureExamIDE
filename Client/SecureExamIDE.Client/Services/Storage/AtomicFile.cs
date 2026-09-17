namespace SecureExamIDE.Client.Services.Storage;

internal static class AtomicFile
{
    // Writes to a temporary file and moves it into place, so a crash half-way through never leaves
    // a truncated file behind. On Linux and macOS the file is readable by its owner only.
    public static async Task WriteAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        string temporaryPath = path + ".tmp";

        await File.WriteAllBytesAsync(temporaryPath, contents, cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    // The same, synchronously, for the workspace's autosave: a source file is a few kilobytes, and
    // a save that has to finish before the exam is handed in cannot wait on a continuation.
    public static void Write(string path, byte[] contents)
    {
        string temporaryPath = path + ".tmp";

        File.WriteAllBytes(temporaryPath, contents);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}
