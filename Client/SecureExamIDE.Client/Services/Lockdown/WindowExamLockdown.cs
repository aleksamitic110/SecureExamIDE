using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Options;

namespace SecureExamIDE.Client.Services.Lockdown;

internal sealed class WindowExamLockdown(IOptions<LockdownOptions> options, TimeProvider timeProvider) : IExamLockdown
{
    private Window? _window;
    private ClipboardGuard? _clipboard;

    // Held as IDisposable on purpose: the hook itself is a Windows-only type, and nothing outside
    // InstallKeyboardHook should be able to touch it on a platform that has none.
#pragma warning disable CA1859
    private IDisposable? _keyboardHook;
#pragma warning restore CA1859
    private WindowState _stateBeforeExam = WindowState.Normal;
    private long _lastRestore;
    private bool _restorePending;
    private bool _closingForEmergencyExit;

    public bool IsActive { get; private set; }

    public event EventHandler? ExamWindowLeft;

    public event EventHandler? EmergencyExitRequested;

    // Raised when something the application did not copy was taken off the clipboard.
    public event EventHandler? OutsideContentBlocked;

    // The window only exists after the services are built, so it is handed over once it does.
    public void Attach(Window window, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _window = window;

        window.Closing += OnClosing;
        window.PropertyChanged += OnWindowPropertyChanged;
        window.Deactivated += OnDeactivated;
        lifetime.ShutdownRequested += OnShutdownRequested;

        // Tunnelling, so the shortcut works even while the editor has the keyboard.
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Dragging a file or a piece of text into the exam is another way in from outside.
        window.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Tunnel);
        window.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Tunnel);

        window.Activated += OnActivated;

        _clipboard = new ClipboardGuard(new WindowClipboard(window));
    }

    public void Enter()
    {
        if (IsActive || _window is null)
        {
            return;
        }

        IsActive = true;
        _stateBeforeExam = _window.WindowState;
        _lastRestore = timeProvider.GetTimestamp();

        if (LockdownOptions.KeepOnTop)
        {
            _window.Topmost = true;
        }

        _window.WindowState = WindowState.FullScreen;

        InstallKeyboardHook();

        // Whatever was on the clipboard before the exam started is not the student's own work.
        _ = ClearOutsideContentAsync();
    }

    public void Exit()
    {
        if (!IsActive || _window is null)
        {
            return;
        }

        IsActive = false;

        _keyboardHook?.Dispose();
        _keyboardHook = null;

        _window.Topmost = false;
        _window.WindowState = _stateBeforeExam == WindowState.FullScreen ? WindowState.Normal : _stateBeforeExam;
    }

    // Alt+F4, the close button of a window manager, a taskbar "Close window": all refused. Only the
    // operating system shutting down gets through - it cannot be refused anyway, and the workspace
    // saves as the student types, so nothing written is lost.
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (IsActive && !_closingForEmergencyExit && e.CloseReason != WindowCloseReason.OSShutdown)
        {
            e.Cancel = true;
        }
    }

    // An application-level quit (Cmd+Q on macOS, for one). Avalonia 12 does not say whether the
    // operating system is behind it, so it is always refused; a real shutdown ends the process anyway.
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (IsActive && !_closingForEmergencyExit)
        {
            e.Cancel = true;
        }
    }

    // Minimised (Win+D, Win+M), restored, un-fullscreened by the window manager: put back, but never
    // more often than once a second. Pushing back immediately, every time, is what turned a second
    // monitor into a frozen desktop: the window manager and the application each undid the other as
    // fast as they could.
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (IsActive && e.Property == Window.WindowStateProperty && _window?.WindowState != WindowState.FullScreen)
        {
            RestoreSoon();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        // Only noticed and counted. Taking the focus back by force is what the student's own window
        // manager fights, and losing that fight costs the whole exam.
        ExamWindowLeft?.Invoke(this, EventArgs.Empty);
    }

    // Coming back to the exam is the moment anything could have been copied from elsewhere, so it is
    // when the clipboard is checked.
    private void OnActivated(object? sender, EventArgs e)
    {
        if (IsActive)
        {
            _ = ClearOutsideContentAsync();
        }
    }

    private async Task ClearOutsideContentAsync()
    {
        if (_clipboard is not null && await _clipboard.RemoveOutsideContentAsync())
        {
            OutsideContentBlocked?.Invoke(this, EventArgs.Empty);
        }
    }

    // Copying and cutting inside the exam is allowed, so what the application puts on the clipboard is
    // remembered and survives the check above.
    private void RememberOwnCopy()
    {
        if (_window?.FocusManager?.GetFocusedElement() is TextBox { SelectedText: { Length: > 0 } selected })
        {
            _clipboard?.Remember(selected);

            return;
        }

        if (_window?.FocusManager?.GetFocusedElement() is AvaloniaEdit.Editing.TextArea { Selection: { IsEmpty: false } selection })
        {
            _clipboard?.Remember(selection.GetText());
        }
    }

    private static bool IsPaste(KeyEventArgs e) =>
        (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V) ||
        (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Insert);

    private async Task PasteOwnContentAsync()
    {
        string? text = _clipboard is null ? null : await _clipboard.TextAllowedToPasteAsync();

        if (text is null)
        {
            OutsideContentBlocked?.Invoke(this, EventArgs.Empty);

            return;
        }

        switch (_window?.FocusManager?.GetFocusedElement())
        {
            case AvaloniaEdit.Editing.TextArea editor:
                editor.Selection.ReplaceSelectionWithText(text);
                break;

            case TextBox box when !box.IsReadOnly:
                int caret = box.CaretIndex;
                string current = box.Text ?? string.Empty;
                box.Text = current[..caret] + text + current[caret..];
                box.CaretIndex = caret + text.Length;
                break;

            default:
                break;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (IsActive)
        {
            e.Handled = true;
            OutsideContentBlocked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void InstallKeyboardHook()
    {
        if (!OperatingSystem.IsWindows())
        {
            // On Linux the compositor owns these keys: X11 would need a keyboard grab and Wayland the
            // shortcuts-inhibit protocol, neither of which Avalonia offers. Written up for the
            // linux-client branch.
            return;
        }

        var hook = new WindowsKeyboardHook();
        hook.Install();
        _keyboardHook = hook;
    }

    private void RestoreSoon()
    {
        if (_restorePending || timeProvider.GetElapsedTime(_lastRestore) < RestoreInterval)
        {
            return;
        }

        _restorePending = true;

        Dispatcher.UIThread.Post(() =>
        {
            _restorePending = false;
            _lastRestore = timeProvider.GetTimestamp();

            if (IsActive && _window is not null && _window.WindowState != WindowState.FullScreen)
            {
                _window.WindowState = WindowState.FullScreen;
            }
        });
    }

    // The escape hatch described in LockdownOptions: save, unlock, close.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsActive && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.C or Key.X)
        {
            RememberOwnCopy();
        }

        // Every paste goes through the application: it is refused here and done again below only when
        // the clipboard holds what the exam itself copied.
        if (IsActive && IsPaste(e))
        {
            e.Handled = true;
            _ = PasteOwnContentAsync();

            return;
        }

        if (!IsActive ||
            !options.Value.AllowEmergencyExit ||
            e.Key != Key.Q ||
            e.KeyModifiers != (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift))
        {
            return;
        }

        e.Handled = true;

        EmergencyExitRequested?.Invoke(this, EventArgs.Empty);

        _closingForEmergencyExit = true;
        Exit();
        _window?.Close();
    }

    private static readonly TimeSpan RestoreInterval = TimeSpan.FromSeconds(1);
}
