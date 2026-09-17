using SecureExamIDE.Client.Services.Exams;

namespace SecureExamIDE.Client.Tests.Exams;

public sealed class LocalExamLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DownloadedExam SampleExam() => new(
        Guid.NewGuid(),
        "Algorithms",
        "Algorithms and Data Structures",
        "Graphs and dynamic programming",
        "Milena Frtunic",
        [new DownloadedSitting(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(3).AddHours(2), 2048, new string('a', 64), DateTimeOffset.UtcNow)],
        [new DownloadedDependency(Guid.NewGuid(), "gcc", "13.2", "application/gzip", 1_000_000, "file.tar.gz")],
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task Load_Should_ReturnWhatWasSaved()
    {
        // Arrange
        var library = new LocalExamLibrary(_directory);
        DownloadedExam exam = SampleExam();
        await library.SaveAsync(exam);

        // Act
        DownloadedExam? loaded = await library.LoadAsync(exam.ExamId);

        // Assert
        loaded.ShouldNotBeNull();
        loaded.Title.ShouldBe(exam.Title);
        loaded.Sittings.ShouldHaveSingleItem().ShouldBe(exam.Sittings[0]);
        loaded.Dependencies.ShouldHaveSingleItem().ShouldBe(exam.Dependencies[0]);
    }

    [Fact]
    public async Task LoadAll_Should_ListEveryExam_AndSkipAnUnreadableOne()
    {
        // Arrange
        var library = new LocalExamLibrary(_directory);
        await library.SaveAsync(SampleExam());
        await library.SaveAsync(SampleExam());

        DownloadedExam broken = SampleExam();
        await library.SaveAsync(broken);
        await File.WriteAllTextAsync(Path.Combine(_directory, "exams", broken.ExamId.ToString("N"), "exam.json"), "{ not json");

        // Act
        IReadOnlyList<DownloadedExam> exams = await library.LoadAllAsync();

        // Assert
        exams.Count.ShouldBe(2);
    }

    // Paths are built from ids only; a name from the server cannot climb out of the exam folder.
    [Fact]
    public void Paths_Should_StayInsideTheExamFolder()
    {
        // Arrange
        var library = new LocalExamLibrary(_directory);
        var examId = Guid.NewGuid();

        // Act
        string dependency = library.DependencyPath(examId, "../../../etc/passwd");
        string fileName = library.DependencyFileName(Guid.NewGuid(), "application/zip");

        // Assert
        Path.GetDirectoryName(dependency).ShouldBe(Path.Combine(_directory, "exams", examId.ToString("N"), "dependencies"));
        fileName.ShouldEndWith(".zip");
        library.PackagePath(examId, Guid.NewGuid()).ShouldStartWith(Path.Combine(_directory, "exams", examId.ToString("N"), "sittings"));
    }
}
