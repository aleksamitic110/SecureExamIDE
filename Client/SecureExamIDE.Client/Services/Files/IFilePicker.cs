namespace SecureExamIDE.Client.Services.Files;

// Choosing a file from this computer. Behind an interface so the screens that use it stay testable:
// the real one needs a window, and a test has neither a window nor a person to click.
public interface IFilePicker
{
    // Null when the person closed the dialog without choosing anything.
    Task<PickedFile?> PickFileAsync(string title, CancellationToken cancellationToken = default);

    // The folder's path, for writing a student's exported files into.
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);
}

public sealed record PickedFile(string Name, string Path, long SizeBytes);
