using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;

namespace SecureExamIDE.Client.Services.Toolchains;

internal sealed class ToolchainService(ILocalExamLibrary library) : IToolchainService
{
    public async Task<ApiResult<IReadOnlyList<Toolchain>>> PrepareAsync(
        DownloadedExam exam,
        CancellationToken cancellationToken = default)
    {
        List<Toolchain> toolchains = [];

        foreach (DownloadedDependency dependency in exam.Dependencies)
        {
            ToolchainKind kind = KindOf(dependency.Name);

            if (kind == ToolchainKind.Unknown)
            {
                // A library or a set of headers: downloaded and unpacked all the same, but nothing
                // in it is a compiler, so it is not offered as one.
                continue;
            }

            string archivePath = library.DependencyPath(exam.ExamId, dependency.FileName);
            string target = Path.Combine(library.ToolsDirectory(exam.ExamId), dependency.DependencyId.ToString("N"));

            ApiResult unpacked = await UnpackAsync(archivePath, target, dependency, cancellationToken);

            if (!unpacked.IsSuccess)
            {
                return ApiResult.Failure<IReadOnlyList<Toolchain>>(unpacked.Error);
            }

            toolchains.Add(Describe(kind, dependency, target));
        }

        // An exam whose toolchain holds no compiler this computer can actually run - the wrong
        // platform's build, or a stand-in - falls back to a compiler installed here, so a student is
        // not left unable to compile. The console says which one is being used.
        if (!toolchains.Any(toolchain => toolchain.CanCompileC || toolchain.CanCompileCpp) &&
            InstalledCompiler() is { } installed)
        {
            toolchains.Add(installed);
        }

        return ApiResult.Success<IReadOnlyList<Toolchain>>(toolchains);
    }

    private static Toolchain? InstalledCompiler()
    {
        string? c = OnPath(OperatingSystem.IsWindows() ? "gcc.exe" : "gcc");
        string? cpp = OnPath(OperatingSystem.IsWindows() ? "g++.exe" : "g++");

        return c is null && cpp is null
            ? null
            : new Toolchain(
                ToolchainKind.Gcc,
                "Compiler installed on this computer",
                string.Empty,
                Path.GetDirectoryName(c ?? cpp!) ?? string.Empty,
                c,
                cpp,
                IsFromThisComputer: true);
    }

    private static string? OnPath(string fileName) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(candidate => File.Exists(candidate) && IsRunnable(candidate));

    // The professor names the dependency; the client recognises the kinds it knows how to drive.
    private static ToolchainKind KindOf(string name) =>
        name.Contains("gcc", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("mingw", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("g++", StringComparison.OrdinalIgnoreCase)
            ? ToolchainKind.Gcc
            : ToolchainKind.Unknown;

    private static async Task<ApiResult> UnpackAsync(
        string archivePath,
        string target,
        DownloadedDependency dependency,
        CancellationToken cancellationToken)
    {
        string marker = Path.Combine(target, ".unpacked");

        // Unpacked already, from the same archive: nothing to do. The size is what tells a re-downloaded
        // archive apart from the one that was unpacked here.
        if (File.Exists(marker) &&
            await File.ReadAllTextAsync(marker, cancellationToken) == dependency.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            return ApiResult.Success();
        }

        if (!File.Exists(archivePath))
        {
            return ApiResult.Failure(ToolchainErrors.CannotUnpack(dependency.Name, "the downloaded archive is missing"));
        }

        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(target);

            await Task.Run(() => Extract(archivePath, target), cancellationToken);

            // A zip carries no Unix permissions, so anything that looks like a program would come out
            // unrunnable. Tar archives keep their modes and are left alone by this.
            MakeExecutable(target);

            await File.WriteAllTextAsync(marker, dependency.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);

            return ApiResult.Success();
        }
        catch (InvalidDataException exception)
        {
            return ApiResult.Failure(ToolchainErrors.CannotUnpack(dependency.Name, exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ApiResult.Failure(ToolchainErrors.CannotUnpack(dependency.Name, exception.Message));
        }
        catch (NotSupportedException)
        {
            return ApiResult.Failure(ToolchainErrors.UnsupportedArchive(dependency.FileName));
        }
    }

    private static void Extract(string archivePath, string target)
    {
        string name = archivePath.ToUpperInvariant();

        if (name.EndsWith(".ZIP", StringComparison.Ordinal))
        {
            // Entries that point outside the folder make this throw, which is exactly what should
            // happen: the archive came from the server, but it was uploaded by a person.
            ZipFile.ExtractToDirectory(archivePath, target, overwriteFiles: true);

            return;
        }

        if (name.EndsWith(".TAR.GZ", StringComparison.Ordinal))
        {
            using FileStream compressed = File.OpenRead(archivePath);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);

            TarFile.ExtractToDirectory(gzip, target, overwriteFiles: true);

            return;
        }

        if (name.EndsWith(".TAR", StringComparison.Ordinal))
        {
            TarFile.ExtractToDirectory(archivePath, target, overwriteFiles: true);

            return;
        }

        // .7z and .tar.xz would need another library; the exam can ship a zip or a tar.gz instead.
        throw new NotSupportedException($"'{Path.GetFileName(archivePath)}' is not a zip or tar archive.");
    }

    private static void MakeExecutable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string parent = Path.GetFileName(Path.GetDirectoryName(file)) ?? string.Empty;

            if (parent is "bin" or "libexec" || Path.GetExtension(file).Length == 0)
            {
                File.SetUnixFileMode(
                    file,
                    File.GetUnixFileMode(file) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
            }
        }
    }

    private static Toolchain Describe(ToolchainKind kind, DownloadedDependency dependency, string root) => new(
        kind,
        dependency.Name,
        dependency.Version,
        root,
        FindProgram(root, OperatingSystem.IsWindows() ? "gcc.exe" : "gcc"),
        FindProgram(root, OperatingSystem.IsWindows() ? "g++.exe" : "g++"));

    // A file with the right name is not yet a compiler: it can be the wrong platform's build, or a
    // stand-in. Asking it for its version settles it before the student presses Run.
    private static bool IsRunnable(string path)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(VersionCheckTimeout))
            {
                process.Kill(entireProcessTree: true);

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or SystemException)
        {
            return false;
        }
    }

    // Archives put their programs in bin/, but not always at the top: a MinGW archive unpacks into a
    // folder of its own first. Searching for the name is simpler than guessing the layout.
    private static string? FindProgram(string root, string fileName) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault(IsRunnable)
            : null;

    private static readonly TimeSpan VersionCheckTimeout = TimeSpan.FromSeconds(10);
}
