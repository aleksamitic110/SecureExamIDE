namespace SecureExamIDE.Client.Services.Lockdown;

public interface IClipboardGuard
{
    // Called when the application itself copies or cuts, so that text can still be pasted inside.
    void Remember(string? text);

    // Wipes anything the application did not put on the clipboard. True when something was wiped,
    // which is what the workspace reports to the student.
    Task<bool> RemoveOutsideContentAsync();

    // What may be pasted: the text when the application itself copied it, and null for anything else.
    Task<string?> TextAllowedToPasteAsync();
}
