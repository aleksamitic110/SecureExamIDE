using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SecureExamIDE.Client.Services.Files;

// The file dialog of the operating system, opened over the main window. Attached once at start-up,
// the same way the exam lockdown is.
internal sealed class WindowFilePicker : IFilePicker
{
    private Window? _window;

    public void Attach(Window window) => _window = window;

    public async Task<PickedFile?> PickFileAsync(string title, CancellationToken cancellationToken = default)
    {
        if (_window is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> chosen = await _window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = title, AllowMultiple = false });

        if (chosen.Count == 0)
        {
            return null;
        }

        IStorageFile file = chosen[0];
        string? path = file.TryGetLocalPath();

        if (path is null)
        {
            // A file the application cannot open by path - inside an archive, or on a phone shared
            // over the network - is refused rather than copied somewhere first.
            return null;
        }

        StorageItemProperties properties = await file.GetBasicPropertiesAsync();

        return new PickedFile(file.Name, path, (long)(properties.Size ?? 0));
    }
}
