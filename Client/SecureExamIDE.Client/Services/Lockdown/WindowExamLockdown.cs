using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace SecureExamIDE.Client.Services.Lockdown;

internal sealed class WindowExamLockdown : IExamLockdown
{
    private Window? _window;
    private WindowState _stateBeforeExam = WindowState.Normal;

    public bool IsActive { get; private set; }

    public event EventHandler? ExamWindowLeft;

    // The window only exists after the services are built, so it is handed over once it does.
    public void Attach(Window window, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _window = window;

        window.Closing += OnClosing;
        window.PropertyChanged += OnWindowPropertyChanged;
        window.Deactivated += OnDeactivated;
        lifetime.ShutdownRequested += OnShutdownRequested;
    }

    public void Enter()
    {
        if (IsActive || _window is null)
        {
            return;
        }

        IsActive = true;
        _stateBeforeExam = _window.WindowState;

        _window.Topmost = true;
        _window.WindowState = WindowState.FullScreen;
        _window.Activate();
    }

    public void Exit()
    {
        if (!IsActive || _window is null)
        {
            return;
        }

        IsActive = false;

        _window.Topmost = false;
        _window.WindowState = _stateBeforeExam == WindowState.FullScreen ? WindowState.Normal : _stateBeforeExam;
    }

    // Alt+F4, the close button of a window manager, a taskbar "Close window": all refused. Only the
    // operating system shutting down gets through - it cannot be refused anyway, and the workspace
    // saves as the student types, so nothing written is lost.
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (IsActive && e.CloseReason != WindowCloseReason.OSShutdown)
        {
            e.Cancel = true;
        }
    }

    // An application-level quit (Cmd+Q on macOS, for one). Avalonia 12 does not say whether the
    // operating system is behind it, so it is always refused; a real shutdown ends the process anyway.
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (IsActive)
        {
            e.Cancel = true;
        }
    }

    // Minimised (Win+D, Win+M), restored, un-fullscreened by the window manager: put back. Posted,
    // because changing the state from inside its own change notification is not reliable.
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (IsActive && e.Property == Window.WindowStateProperty && _window?.WindowState != WindowState.FullScreen)
        {
            Dispatcher.UIThread.Post(RestoreExamWindow);
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        ExamWindowLeft?.Invoke(this, EventArgs.Empty);
        Dispatcher.UIThread.Post(RestoreExamWindow);
    }

    private void RestoreExamWindow()
    {
        if (!IsActive || _window is null)
        {
            return;
        }

        _window.WindowState = WindowState.FullScreen;
        _window.Activate();
    }
}
