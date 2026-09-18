using System.Security.Cryptography;
using System.Text;

namespace SecureExamIDE.Client.Services.Lockdown;

// "Paste into the application from outside is blocked; copy and paste inside the application is
// allowed" - the rule the exam mode exists for.
//
// How it works: the application remembers what it last put on the clipboard itself. Copying from
// anywhere else means leaving the exam window, and when the window comes back the clipboard is read:
// anything the application did not put there is wiped, so there is nothing left to paste. Text copied
// inside the exam survives, because it is what the application remembers.
//
// Wiping alone is not enough everywhere: a clipboard manager (KDE's Klipper, for one) puts back what
// an application clears, and on Linux that defeated the first version of this. So the paste itself is
// also checked - the application performs the paste only when the clipboard holds what it copied.
//
// Only the text is compared, through a digest, so nothing copied is kept in memory a second time.
internal sealed class ClipboardGuard(IClipboardAccess clipboard) : IClipboardGuard
{
    private byte[]? _ownContent;

    public void Remember(string? text) =>
        _ownContent = string.IsNullOrEmpty(text) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(text));

    public async Task<bool> RemoveOutsideContentAsync()
    {
        string? text = await clipboard.ReadTextAsync();

        if (string.IsNullOrEmpty(text) || IsOwnContent(text))
        {
            return false;
        }

        await clipboard.ClearAsync();
        _ownContent = null;

        return true;
    }

    public async Task<string?> TextAllowedToPasteAsync()
    {
        string? text = await clipboard.ReadTextAsync();

        return !string.IsNullOrEmpty(text) && IsOwnContent(text) ? text : null;
    }

    private bool IsOwnContent(string text) =>
        _ownContent is not null &&
        CryptographicOperations.FixedTimeEquals(_ownContent, SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
