using System.IO.Compression;
using System.Text;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Toolchains;
using SecureExamIDE.Client.Tests.Run;

namespace SecureExamIDE.Client.Tests.Toolchains;

public sealed class ToolchainServiceTests : IDisposable
{
    private static readonly Guid ExamId = Guid.NewGuid();

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalExamLibrary _library;
    private readonly ToolchainService _service;

    public ToolchainServiceTests()
    {
        _library = new LocalExamLibrary(_directory);
        _service = new ToolchainService(_library);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // A downloaded archive, written where the download service would have put it.
    private DownloadedDependency WriteArchive(string name, string version, string fileName, params string[] entries)
    {
        string path = _library.DependencyPath(ExamId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (FileStream file = File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (string entry in entries)
            {
                using Stream content = archive.CreateEntry(entry).Open();
                content.Write(Encoding.UTF8.GetBytes($"pretend this is {entry}"));
            }
        }

        return new DownloadedDependency(Guid.NewGuid(), name, version, "application/zip", new FileInfo(path).Length, fileName);
    }

    private static DownloadedExam ExamWith(params DownloadedDependency[] dependencies) => new(
        ExamId, "Algorithms", "Algorithms and Data Structures", "", "Milena Frtunic", [], dependencies, DateTimeOffset.UtcNow);

    private static string Compiler => OperatingSystem.IsWindows() ? "bin/gcc.exe" : "bin/gcc";

    private static string CppCompiler => OperatingSystem.IsWindows() ? "bin/g++.exe" : "bin/g++";

    [Fact]
    public async Task Prepare_Should_UnpackTheToolchain_AndFindItsCompilers()
    {
        // Arrange
        DownloadedDependency gcc = WriteArchive("GCC (MinGW-w64)", "14.2.0", "gcc.zip", Compiler, CppCompiler, "README.txt");

        // Act
        ApiResult<IReadOnlyList<Toolchain>> result = await _service.PrepareAsync(ExamWith(gcc));

        // Assert
        Toolchain toolchain = result.Value[0];
        toolchain.Kind.ShouldBe(ToolchainKind.Gcc);
        toolchain.Name.ShouldBe("GCC (MinGW-w64)");
        toolchain.RootDirectory.ShouldNotBeEmpty();

        // The archive was unpacked where the exam keeps its tools.
        Directory.EnumerateFiles(toolchain.RootDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ShouldContain(Path.GetFileName(Compiler));
    }

    // MinGW archives unpack into a folder of their own, so the compiler is not at the top.
    // A file called gcc is not yet a compiler. The demo exams ship stand-ins, and an archive can be
    // for the wrong platform, so what is found is asked for its version before it is offered.
    [InstalledCompilerFact]
    public async Task Prepare_Should_FallBackToTheCompilerInstalledHere_WhenTheExamsIsNotRunnable()
    {
        // Arrange - the archive contains a text file named like a compiler.
        DownloadedDependency gcc = WriteArchive("GCC", "14.2.0", "gcc.zip", Compiler);

        // Act
        ApiResult<IReadOnlyList<Toolchain>> result = await _service.PrepareAsync(ExamWith(gcc));

        // Assert
        Toolchain toolchain = result.Value[^1];
        toolchain.IsFromThisComputer.ShouldBeTrue();
        toolchain.CanCompileC.ShouldBeTrue();
        result.Value.First(candidate => !candidate.IsFromThisComputer).CanCompileC.ShouldBeFalse();
    }

    [Fact]
    public async Task Prepare_Should_FindACompilerBelowAFolderOfItsOwn()
    {
        // Arrange
        DownloadedDependency gcc = WriteArchive(
            "GCC", "14.2.0", "gcc.zip", $"mingw64/{Compiler}", $"mingw64/{CppCompiler}");

        // Act
        ApiResult<IReadOnlyList<Toolchain>> result = await _service.PrepareAsync(ExamWith(gcc));

        // Assert - unpacked into the folder the archive brought with it.
        Directory.EnumerateDirectories(result.Value[0].RootDirectory).Select(Path.GetFileName).ShouldContain("mingw64");
    }

    // Unpacking happens once: exam day must not start with a wait.
    [Fact]
    public async Task Prepare_Should_NotUnpackAgain_WhenTheArchiveIsAlreadyUnpacked()
    {
        // Arrange
        DownloadedDependency gcc = WriteArchive("GCC", "14.2.0", "gcc.zip", Compiler);
        ApiResult<IReadOnlyList<Toolchain>> first = await _service.PrepareAsync(ExamWith(gcc));
        string unpackedFile = Directory.EnumerateFiles(first.Value[0].RootDirectory, "*", SearchOption.AllDirectories).First();
        DateTime unpackedAt = File.GetLastWriteTimeUtc(unpackedFile);

        // Act
        await Task.Delay(50);
        await _service.PrepareAsync(ExamWith(gcc));

        // Assert
        File.GetLastWriteTimeUtc(unpackedFile).ShouldBe(unpackedAt);
    }

    [Fact]
    public async Task Prepare_Should_IgnoreADependencyThatIsNotACompiler()
    {
        // Arrange
        DownloadedDependency headers = WriteArchive("Course headers", "1.0", "headers.zip", "include/course.h");

        // Act
        ApiResult<IReadOnlyList<Toolchain>> result = await _service.PrepareAsync(ExamWith(headers));

        // Assert - no compiler came from the exam; anything offered is this computer's own.
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldAllBe(toolchain => toolchain.IsFromThisComputer);
    }

    [Fact]
    public async Task Prepare_Should_ExplainAFormatItCannotUnpack()
    {
        // Arrange
        DownloadedDependency gcc = WriteArchive("GCC", "14.2.0", "gcc.7z", Compiler);

        // Act
        ApiResult<IReadOnlyList<Toolchain>> result = await _service.PrepareAsync(ExamWith(gcc));

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("Toolchains.UnsupportedArchive");
    }

    [Fact]
    public async Task Prepare_Should_ExplainWhenTheArchiveWasNeverDownloaded()
    {
        // Arrange
        var missing = new DownloadedDependency(Guid.NewGuid(), "GCC", "14.2.0", "application/zip", 10, "gcc.zip");

        // Act
        ApiResult<IReadOnlyList<Toolchain>> result = await _service.PrepareAsync(ExamWith(missing));

        // Assert
        result.Error!.Code.ShouldBe("Toolchains.CannotUnpack");
    }
}
