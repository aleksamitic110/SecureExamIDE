using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Toolchains;

internal static class ToolchainErrors
{
    // Said to the student, who can do nothing about it but tell the professor - so it names the
    // toolchain as the professor named it, not the file it was stored as.
    public static ApiError UnsupportedArchive(string name) => new(
        0,
        "Toolchains.UnsupportedArchive",
        $"'{name}' was attached to this exam in a format the application cannot unpack. Only zip and tar.gz archives can be used; the professor has to attach it again as one of those.",
        []);

    // Said to the professor, at the moment the mistake can still be put right.
    public static ApiError UnsupportedUpload(string fileName) => new(
        0,
        "Toolchains.UnsupportedUpload",
        $"'{fileName}' cannot be attached: the students' application unpacks only .zip and .tar.gz archives. Download the .zip build of the same toolchain and attach that instead.",
        []);

    public static ApiError CannotUnpack(string name, string detail) => new(
        0,
        "Toolchains.CannotUnpack",
        $"'{name}' could not be unpacked: {detail}",
        []);
}
