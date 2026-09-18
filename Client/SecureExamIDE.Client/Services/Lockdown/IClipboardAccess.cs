namespace SecureExamIDE.Client.Services.Lockdown;

// The system clipboard, behind an interface so the rule about what may be pasted can be tested
// without a window.
public interface IClipboardAccess
{
    Task<string?> ReadTextAsync();

    Task ClearAsync();
}
