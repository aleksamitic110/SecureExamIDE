using Microsoft.Extensions.Time.Testing;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Downloads;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.Tests.Exams;

public sealed class ExamDownloadServiceTests : IDisposable
{
    private static readonly CatalogExam Exam = new(
        Guid.NewGuid(), "Algorithms", "Graphs", "Algorithms and Data Structures", DateTimeOffset.UtcNow,
        "Milena", "Frtunic", 2, 1, 5000);

    private static readonly ExamSitting Sitting = new(
        Guid.NewGuid(), Exam.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(2), 1000, new string('b', 64));

    private static readonly ExamDependency Gcc = new(Guid.NewGuid(), "gcc", "13.2", DependencyPlatform.LinuxX64, "application/gzip", 4000);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IFileDownloader _downloader = Substitute.For<IFileDownloader>();
    private readonly LocalExamLibrary _library;

    public ExamDownloadServiceTests()
    {
        _library = new LocalExamLibrary(_directory);

        _session.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(ApiResult.Success("token"));
        _catalog.GetDependenciesAsync(Exam.Id, Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success<IReadOnlyList<ExamDependency>>([Gcc]));
        _api.GetSittingPackageAsync(Sitting.Id, "token", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new SittingPackage(
                Sitting.Id, new Uri("http://storage.test/p.bin"), new Uri("http://storage.test/p.hdr"),
                DateTimeOffset.UtcNow.AddHours(1), Sitting.PackageSizeBytes, Sitting.PackageSha256)));
        _api.GetDependencyDownloadAsync(Gcc.Id, "token", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new DependencyDownload(
                Gcc.Id, Gcc.Name, Gcc.Version, Gcc.Platform, Gcc.ContentType, Gcc.SizeBytes, new Uri("http://storage.test/gcc"), DateTimeOffset.UtcNow.AddHours(2))));

        // Behaves like the real downloader on success: the file ends up at its destination.
        _downloader.DownloadAsync(Arg.Any<DownloadRequest>(), Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                DownloadRequest request = call.Arg<DownloadRequest>();
                Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
                File.WriteAllBytes(request.DestinationPath, new byte[request.ExpectedSizeBytes ?? 10]);
                return ApiResult.Success();
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ExamDownloadService CreateService() =>
        new(_api, _session, _catalog, _downloader, _library, new FakeTimeProvider(DateTimeOffset.UtcNow));

    [Fact]
    public async Task Download_Should_FetchPackageHeaderAndDependencies_AndRecordTheSitting()
    {
        // Arrange
        List<ExamDownloadProgress> reports = [];

        // Act
        ApiResult result = await CreateService().DownloadSittingAsync(Exam, Sitting, new InlineProgress(reports.Add));

        // Assert
        result.IsSuccess.ShouldBeTrue();

        await _downloader.Received(1).DownloadAsync(
            Arg.Is<DownloadRequest>(r => r.Url.AbsolutePath == "/p.bin" && r.ExpectedSha256 == Sitting.PackageSha256 && r.ExpectedSizeBytes == 1000),
            Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>());
        await _downloader.Received(1).DownloadAsync(
            Arg.Is<DownloadRequest>(r => r.Url.AbsolutePath == "/p.hdr"), Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>());
        await _downloader.Received(1).DownloadAsync(
            Arg.Is<DownloadRequest>(r => r.Url.AbsolutePath == "/gcc" && r.ExpectedSizeBytes == 4000 && r.ExpectedSha256 == null),
            Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>());

        DownloadedExam local = (await _library.LoadAsync(Exam.Id)).ShouldNotBeNull();
        local.Sittings.ShouldHaveSingleItem().SittingId.ShouldBe(Sitting.Id);
        local.Dependencies.ShouldHaveSingleItem().Name.ShouldBe("gcc");
        local.ProfessorName.ShouldBe("Milena Frtunic");

        reports[^1].ItemNumber.ShouldBe(3);
        reports[^1].BytesTotal.ShouldBe(5000);
    }

    // A second sitting of the same exam needs the same toolchains; they are not pulled twice.
    [Fact]
    public async Task Download_Should_ReuseADependencyAlreadyOnDisk()
    {
        // Arrange
        string path = _library.DependencyPath(Exam.Id, _library.DependencyFileName(Gcc.Id, Gcc.ContentType));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[Gcc.SizeBytes]);

        // Act
        ApiResult result = await CreateService().DownloadSittingAsync(Exam, Sitting);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        await _api.DidNotReceiveWithAnyArgs().GetDependencyDownloadAsync(Guid.Empty, default!, default);
    }

    [Fact]
    public async Task Download_Should_RecordNothing_WhenAFileFails()
    {
        // Arrange
        _downloader.DownloadAsync(
                Arg.Is<DownloadRequest>(r => r.Url.AbsolutePath == "/gcc"), Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure(new ApiError(0, "Download.Interrupted", "Interrupted.", [])));

        // Act
        ApiResult result = await CreateService().DownloadSittingAsync(Exam, Sitting);

        // Assert
        result.Error!.Code.ShouldBe("Download.Interrupted");
        (await _library.LoadAsync(Exam.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Download_Should_StopBeforeAnyFile_WhenTheSittingWasCancelled()
    {
        // Arrange
        _api.GetSittingPackageAsync(Sitting.Id, "token", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<SittingPackage>(new ApiError(409, ErrorCodes.SittingCancelled, "Cancelled.", [])));

        // Act
        ApiResult result = await CreateService().DownloadSittingAsync(Exam, Sitting);

        // Assert
        result.Error!.Code.ShouldBe(ErrorCodes.SittingCancelled);
        await _downloader.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default, default);
    }

    [Fact]
    public async Task Download_Should_KeepAnEarlierSittingOfTheSameExam()
    {
        // Arrange
        ExamSitting retake = Sitting with { Id = Guid.NewGuid(), StartsAt = Sitting.StartsAt.AddMonths(1), EndsAt = Sitting.EndsAt.AddMonths(1) };
        _api.GetSittingPackageAsync(retake.Id, "token", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new SittingPackage(
                retake.Id, new Uri("http://storage.test/r.bin"), new Uri("http://storage.test/r.hdr"),
                DateTimeOffset.UtcNow.AddHours(1), retake.PackageSizeBytes, retake.PackageSha256)));
        ExamDownloadService service = CreateService();

        // Act
        await service.DownloadSittingAsync(Exam, Sitting);
        await service.DownloadSittingAsync(Exam, retake);

        // Assert
        DownloadedExam local = (await _library.LoadAsync(Exam.Id)).ShouldNotBeNull();
        local.Sittings.Select(s => s.SittingId).ShouldBe([Sitting.Id, retake.Id]);
    }

    private sealed class InlineProgress(Action<ExamDownloadProgress> report) : IProgress<ExamDownloadProgress>
    {
        public void Report(ExamDownloadProgress value) => report(value);
    }
}
