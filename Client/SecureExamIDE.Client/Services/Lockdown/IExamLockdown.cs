namespace SecureExamIDE.Client.Services.Lockdown;

// Keeps the application fullscreen and open while an exam is in progress. Entered when the workspace
// opens and left only when the student finishes the exam.
//
// What an ordinary application can do stops at its own window: it can refuse to close and go back to
// fullscreen when something changes it. It cannot stop the operating system switching away (Alt+Tab,
// the Windows key) or Ctrl+Alt+Del. Blocking those needs a Windows keyboard hook, which belongs to the
// lockdown step; until then leaving the window is noticed and reported.
public interface IExamLockdown
{
    bool IsActive { get; }

    // Raised each time the exam window loses the focus while locked.
    event EventHandler? ExamWindowLeft;

    // Raised when the emergency exit is used, so the workspace can save before the application closes.
    event EventHandler? EmergencyExitRequested;

    // Raised when something that came from outside the exam was taken off the clipboard, or a drop
    // was refused, so the student is told why the paste did nothing.
    event EventHandler? OutsideContentBlocked;

    void Enter();

    void Exit();
}
