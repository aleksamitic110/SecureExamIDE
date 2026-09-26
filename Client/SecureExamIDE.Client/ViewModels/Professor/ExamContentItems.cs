using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One task file of an exam, as the contents screen shows it.
public sealed class ExamFileItem(ExamFileInfo file)
{
    public Guid Id => file.Id;

    public string FileName => file.FileName;

    public string Details => $"{ByteSize.Format(file.SizeBytes)} · {file.ContentType}";

    // The first characters are enough to compare against a digest by eye; the whole thing is 64.
    public string Digest => $"SHA-256 {file.Sha256[..12]}…";
}

// One toolchain of an exam. It carries no digest by design - the API never sees its bytes.
public sealed class ExamDependencyItem(ExamDependency dependency)
{
    public Guid Id => dependency.Id;

    public string Name => $"{dependency.Name} {dependency.Version}";

    public string Platform => dependency.Platform switch
    {
        DependencyPlatform.WindowsX64 => "Windows (64-bit)",
        DependencyPlatform.LinuxX64 => "Linux (64-bit)",
        _ => "Any computer"
    };

    public string Details => $"{ByteSize.Format(dependency.SizeBytes)} · {dependency.ContentType}";
}

// One choice in the platform picker. A compiler is a native program, so which computers it runs on
// has to be stated: the server cannot tell a MinGW zip from a Linux one and takes the professor's word.
public sealed record PlatformOption(string Label, DependencyPlatform Value)
{
    public static readonly IReadOnlyList<PlatformOption> Options =
    [
        new("Windows (64-bit)", DependencyPlatform.WindowsX64),
        new("Linux (64-bit)", DependencyPlatform.LinuxX64),
        new("Any computer", DependencyPlatform.Any)
    ];

    public override string ToString() => Label;
}
