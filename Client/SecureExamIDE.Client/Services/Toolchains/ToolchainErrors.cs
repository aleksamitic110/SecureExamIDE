using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Toolchains;

internal static class ToolchainErrors
{
    public static ApiError UnsupportedArchive(string fileName) => new(
        0,
        "Toolchains.UnsupportedArchive",
        $"'{fileName}' is in a format this version cannot unpack. Zip and tar.gz archives are supported.",
        []);

    public static ApiError CannotUnpack(string name, string detail) => new(
        0,
        "Toolchains.CannotUnpack",
        $"'{name}' could not be unpacked: {detail}",
        []);

    public static readonly ApiError NoCompiler = new(
        0,
        "Toolchains.NoCompiler",
        "No C or C++ compiler was found for this exam on this computer.",
        []);
}
