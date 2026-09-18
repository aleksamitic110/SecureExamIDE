using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SecureExamIDE.Client.Services.Lockdown;

// Blocks the ways out of a fullscreen window that Windows itself provides: Alt+Tab, the Windows key,
// Ctrl+Esc, Alt+Esc. A low-level keyboard hook sees every key before the system does and can swallow
// it, which is how kiosk and exam applications do this.
//
// **Ctrl+Alt+Del cannot be blocked by anything** - Windows reserves it - and neither can a hook stop
// a determined student who stops the process. What it does stop is the accidental and the casual, and
// every attempt is still counted through the window losing focus.
//
// The hook must be installed on a thread with a message loop; the UI thread has one.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsKeyboardHook : IDisposable
{
    private IntPtr _hook;

    // Kept alive deliberately: the delegate is passed to Windows, and a collected one would crash.
    private readonly HookProcedure _procedure;

    public WindowsKeyboardHook() => _procedure = OnKey;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (IsInstalled)
        {
            return;
        }

        _hook = SetWindowsHookExW(WhKeyboardLowLevel, _procedure, IntPtr.Zero, 0);
    }

    public void Remove()
    {
        if (!IsInstalled)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Remove();

    private IntPtr OnKey(int code, IntPtr message, ref KeyboardInput input)
    {
        if (code >= 0 && IsBlocked(input))
        {
            // Swallowed: the system never sees it.
            return 1;
        }

        return CallNextHookEx(IntPtr.Zero, code, message, ref input);
    }

    private static bool IsBlocked(KeyboardInput input)
    {
        bool altHeld = (input.Flags & AltHeld) != 0;
        bool controlHeld = (GetKeyState(VkControl) & 0x8000) != 0;

        return input.VirtualKey switch
        {
            VkLeftWindows or VkRightWindows => true,
            VkTab when altHeld => true,
            VkEscape when altHeld || controlHeld => true,
            _ => false
        };
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr SetWindowsHookExW(int hookId, HookProcedure procedure, IntPtr module, uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, ref KeyboardInput input);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetKeyState(int virtualKey);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProcedure(int code, IntPtr message, ref KeyboardInput input);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private const int WhKeyboardLowLevel = 13;
    private const uint AltHeld = 0x20;
    private const uint VkTab = 0x09;
    private const uint VkEscape = 0x1B;
    private const uint VkLeftWindows = 0x5B;
    private const uint VkRightWindows = 0x5C;
    private const int VkControl = 0x11;
}
