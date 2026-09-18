using System.Runtime.InteropServices;

namespace SecureExamIDE.Client.Services.Api;

// Which computers a toolchain runs on, sent and received as text the way the API serialises it.
// A compiler is a native program: the Windows build of GCC (MinGW-w64) is useless on Linux and the
// other way round, so each client asks for its own.
public enum DependencyPlatform
{
    Any,
    WindowsX64,
    LinuxX64
}

public static class ClientPlatform
{
    // What this computer can run. Null on anything else - an ARM laptop, macOS - where only the
    // toolchains marked Any make sense, and the exam's compilers are not offered at all.
    public static DependencyPlatform? Current
    {
        get
        {
            if (RuntimeInformation.OSArchitecture != Architecture.X64)
            {
                return null;
            }

            if (OperatingSystem.IsWindows())
            {
                return DependencyPlatform.WindowsX64;
            }

            return OperatingSystem.IsLinux() ? DependencyPlatform.LinuxX64 : null;
        }
    }
}
