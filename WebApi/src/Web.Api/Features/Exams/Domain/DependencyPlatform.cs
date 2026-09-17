namespace Web.Api.Features.Exams;

// Which computers a dependency runs on. A compiler is a native program, so a GCC built for Linux
// is useless on Windows (where GCC means MinGW-w64), and a JDK or a Python build differs per
// operating system too. The professor uploads one archive per platform, and each client downloads
// only the ones it can run.
//
// Persisted as text, like Role, so the column stays readable and reordering the members cannot
// silently repurpose a row. macOS is left out until a client exists for it; a new member needs
// no migration, since the column is text.
public enum DependencyPlatform
{
    // Runs anywhere - a library of source files, a set of headers, a pure-Java jar.
    Any = 1,

    WindowsX64 = 2,

    LinuxX64 = 3
}
