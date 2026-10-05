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
            // Everything is unpacked, a set of course headers as much as a compiler: the student may
            // well have to include it. What the kind decides is only which programs are looked for.
            ToolchainKind kind = KindOf(dependency.Name);

            string archivePath = library.DependencyPath(exam.ExamId, dependency.FileName);
            string target = Path.Combine(library.ToolsDirectory(exam.ExamId), dependency.DependencyId.ToString("N"));

            ApiResult unpacked = await UnpackAsync(archivePath, target, dependency, cancellationToken);

            if (!unpacked.IsSuccess)
            {
                // A compiler that will not unpack has to be said out loud. A library that will not is
                // not worth stopping the run for: it may not even be an archive, and nothing has to be
                // unpacked out of it before the student can compile.
                if (kind != ToolchainKind.Unknown)
                {
                    return ApiResult.Failure<IReadOnlyList<Toolchain>>(unpacked.Error);
                }

                continue;
            }

            toolchains.Add(Describe(kind, dependency, target));
        }

        // An exam whose toolchain holds no program this computer can actually run - the wrong
        // platform's build, or a stand-in - falls back to a compiler installed here, so a student is
        // not left unable to compile. The console says which one is being used.
        if (!toolchains.Any(toolchain => toolchain.Programs.Count > 0) &&
            InstalledCompiler() is { } installed)
        {
            toolchains.Add(installed);
        }

        return ApiResult.Success<IReadOnlyList<Toolchain>>(toolchains);
    }

    private static Toolchain? InstalledCompiler()
    {
        Dictionary<ToolName, string> found = [];

        if (OnPath("gcc") is { } c)
        {
            found[ToolName.CCompiler] = c;
        }

        if (OnPath("g++") is { } cpp)
        {
            found[ToolName.CppCompiler] = cpp;
        }

        return found.Count == 0
            ? null
            : new Toolchain(
                ToolchainKind.Gcc,
                "Compiler installed on this computer",
                string.Empty,
                Path.GetDirectoryName(found.Values.First()) ?? string.Empty,
                found,
                IsFromThisComputer: true);
    }

    // Directory by directory in the order PATH lists them, so the machine's own precedence decides -
    // and only then does a real binary win over a script launcher.
    private static string? OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => FileNamesOf(program).Select(name => Path.Combine(directory, name)))
            .FirstOrDefault(candidate => File.Exists(candidate) && IsRunnable(candidate));

    // The professor names the dependency; the client recognises the kinds it knows how to look inside.
    // A kind this version cannot yet build with is still recognised and unpacked - what it can be built
    // with is the language drivers' business, and a toolchain no driver wants is simply never chosen.
    private static ToolchainKind KindOf(string name) =>
        Known.FirstOrDefault(known =>
            known.Names.Any(needle => name.Contains(needle, StringComparison.OrdinalIgnoreCase))).Kind;

    private static readonly (ToolchainKind Kind, string[] Names)[] Known =
    [
        (ToolchainKind.Gcc, ["gcc", "mingw", "g++"]),
        (ToolchainKind.Jdk, ["jdk", "openjdk", "java"]),
        (ToolchainKind.Python, ["python", "cpython"]),
        (ToolchainKind.DotnetSdk, ["dotnet", ".net", "sdk"])
    ];

    // Which programs each kind is made of, and what the client calls them. A new language needs a row
    // here and a driver that asks for these names; nothing else in the service changes.
    private static readonly Dictionary<ToolchainKind, (ToolName Tool, string Program)[]> KindPrograms = new()
    {
        [ToolchainKind.Gcc] = [(ToolName.CCompiler, "gcc"), (ToolName.CppCompiler, "g++")],
        [ToolchainKind.Jdk] = [(ToolName.JavaCompiler, "javac"), (ToolName.JavaRuntime, "java")],
        [ToolchainKind.Python] = [(ToolName.PythonRuntime, "python")],
        [ToolchainKind.DotnetSdk] = [(ToolName.DotnetSdk, "dotnet")]
    };

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

    private static Toolchain Describe(ToolchainKind kind, DownloadedDependency dependency, string root)
    {
        Dictionary<ToolName, string> found = [];

        foreach ((ToolName tool, string program) in KindPrograms.GetValueOrDefault(kind, []))
        {
            if (FindProgram(root, program) is { } path)
            {
                found[tool] = path;
            }
        }

        return new Toolchain(kind, dependency.Name, dependency.Version, root, found);
    }

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
    private static string? FindProgram(string root, string program) =>
        Directory.Exists(root)
            ? FileNamesOf(program)
                .SelectMany(name => Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
                .FirstOrDefault(IsRunnable)
            : null;

    // On Windows a toolchain may ship a script launcher rather than the binary itself - a JDK and the
    // .NET SDK both do - and CreateProcess starts a .cmd as readily as an .exe. A real binary is
    // preferred where both are present, which is why the extensions are tried in this order.
    private static string[] FileNamesOf(string program) =>
        OperatingSystem.IsWindows()
            ? [program + ".exe", program + ".cmd", program + ".bat"]
            : [program];

    private static readonly TimeSpan VersionCheckTimeout = TimeSpan.FromSeconds(10);
}
