namespace Web.Api.Features.Exams;

// Settings that govern exam content. Bound from the "Exams" configuration section.
public sealed class ExamOptions
{
    // Ceiling for a single dependency. It cannot be enforced while the upload happens - a presigned
    // URL lets the client PUT whatever it likes straight to storage - so it is checked at commit
    // time instead, and anything over it is deleted rather than recorded.
    public long MaxDependencyBytes { get; set; }

    public long ResolvedMaxDependencyBytes =>
        MaxDependencyBytes > 0 ? MaxDependencyBytes : DefaultMaxDependencyBytes;

    // 2 GiB: comfortably above a JDK or a GCC toolchain, far below anything that would fill the
    // disk by accident.
    public const long DefaultMaxDependencyBytes = 2L * 1024 * 1024 * 1024;
}
