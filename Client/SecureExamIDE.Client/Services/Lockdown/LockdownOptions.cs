namespace SecureExamIDE.Client.Services.Lockdown;

public sealed class LockdownOptions
{
    // A testing build. It switches on the escape hatch - Ctrl+Alt+Shift+Q saves the work, leaves the
    // lockdown and closes the application - and lets a finished sitting be opened again with its code.
    // Both exist because testing an exam must not cost a restart of the computer, or a fresh sitting
    // every time; in a real exam either would be a way around the rules.
    //
    // A Debug build switches it on in the composition root; a Release build needs
    // "Lockdown:AllowEmergencyExit" in configuration.
    public bool AllowEmergencyExit { get; set; }

    // Keeping the exam window on top fights the window manager on Linux, where a fullscreen window is
    // already above everything the student needs to be kept away from. On Windows it is what stops
    // another window being placed over the exam.
    public static bool KeepOnTop => OperatingSystem.IsWindows();
}
