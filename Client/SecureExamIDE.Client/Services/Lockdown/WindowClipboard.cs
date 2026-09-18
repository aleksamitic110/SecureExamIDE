using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace SecureExamIDE.Client.Services.Lockdown;

internal sealed class WindowClipboard(Window window) : IClipboardAccess
{
    public async Task<string?> ReadTextAsync()
    {
        IClipboard? clipboard = window.Clipboard;

        return clipboard is null ? null : await clipboard.TryGetTextAsync();
    }

    public async Task ClearAsync()
    {
        if (window.Clipboard is { } clipboard)
        {
            await clipboard.ClearAsync();
        }
    }
}
