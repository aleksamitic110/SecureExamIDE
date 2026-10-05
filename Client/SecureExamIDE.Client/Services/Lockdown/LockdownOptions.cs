namespace SecureExamIDE.Client.Services.Lockdown;

public sealed class LockdownOptions
{
    // A testing build. It switches on the escape hatch - Ctrl+Alt+Shift+Q saves the work, leaves the
    // lockdown and closes the application - because testing an exam must not cost a restart of the
    // computer; in a real exam it would be a way around the rules. Opening the sitting again afterwards
    // is recorded in the activity log like any other reopening, and a finished sitting stays closed.
    //
    // A Debug build switches it on in the composition root; a Release build needs
    // "Lockdown:AllowEmergencyExit" in configuration.
    public bool AllowEmergencyExit { get; set; }

    // Keeping the exam window on top fights the window manager on Linux, where a fullscreen window is
    // already above everything the student needs to be kept away from. On Windows it is what stops
    // another window being placed over the exam.
    public static bool KeepOnTop => OperatingSystem.IsWindows();
}
