using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Uploads;

namespace SecureExamIDE.Client.Tests.Exams;

public sealed class ExamContentServiceTests : IDisposable
{
    private static readonly Guid ExamId = Guid.NewGuid();

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly ISessionService _session = Substitute.For<ISessionService>();
    private readonly IFileUploader _uploader = Substitute.For<IFileUploader>();
    private readonly ExamContentService _service;
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"sei-{Guid.NewGuid():N}.zip");

    public ExamContentServiceTests()
    {
        File.WriteAllBytes(_file, new byte[64]);
        _session.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(ApiResult.Success("token-value"));
        _service = new ExamContentService(_api, _session, _uploader);
    }

    public void Dispose() => File.Delete(_file);

    // A task file is proxied through the API, and the commit carries the object key alone - the
    // digest the server measured in phase one is never resent.
    [Fact]
    public async Task AddTaskFile_Should_UploadThenCommit()
    {
        // Arrange
        _api.UploadExamFileContentAsync(
                ExamId, "tasks.pdf", Arg.Any<Stream>(), Arg.Any<string>(), "token-value", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new UploadedExamFile("exams/k/files/abc", 64, new string('a', 64))));
        _api.AddExamFileAsync(ExamId, "exams/k/files/abc", "tasks.pdf", "token-value", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(Guid.NewGuid()));

        // Act
        ApiResult result = await _service.AddTaskFileAsync(ExamId, _file, "tasks.pdf");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        await _api.Received().AddExamFileAsync(
            ExamId, "exams/k/files/abc", "tasks.pdf", "token-value", Arg.Any<CancellationToken>());
    }

    // Nothing is committed when the bytes did not arrive: that is the whole point of two phases.
    [Fact]
    public async Task AddTaskFile_Should_NotCommit_WhenTheUploadFails()
    {
        // Arrange
        _api.UploadExamFileContentAsync(
                ExamId, Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), "token-value", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<UploadedExamFile>(new ApiError(413, "Http.413", "Too large.", [])));

        // Act
        ApiResult result = await _service.AddTaskFileAsync(ExamId, _file, "tasks.pdf");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error!.StatusCode.ShouldBe(413);
        await _api.DidNotReceive().AddExamFileAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddTaskFile_Should_ReportADiskProblem_WhenTheFileCannotBeRead()
    {
        // Act
        ApiResult result = await _service.AddTaskFileAsync(ExamId, Path.Combine(Path.GetTempPath(), "no-such-file"), "x.pdf");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("Upload.DiskError");
    }

    // A toolchain takes the other route: a presigned link, the bytes straight to storage, then the
    // commit that the API checks against what is actually there.
    [Fact]
    public async Task AddToolchain_Should_AskForALink_UploadToIt_ThenCommit()
    {
        // Arrange
        var uploadUrl = new Uri("http://localhost:9000/exam-packages/d.zip?X-Amz-Signature=abc");
        _api.CreateDependencyUploadUrlAsync(ExamId, "token-value", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new DependencyUploadTarget("exams/k/dependencies/abc", uploadUrl, DateTimeOffset.UtcNow.AddHours(2))));
        _uploader.UploadAsync(Arg.Any<UploadRequest>(), Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success());
        _api.AddExamDependencyAsync(
                ExamId, "exams/k/dependencies/abc", "GCC", "14.2.0", DependencyPlatform.LinuxX64, "token-value",
                Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(Guid.NewGuid()));

        // Act
        ApiResult result = await _service.AddToolchainAsync(
            ExamId, _file, "GCC", "14.2.0", DependencyPlatform.LinuxX64);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        await _uploader.Received().UploadAsync(
            Arg.Is<UploadRequest>(r => r.UploadUrl == uploadUrl && r.SourcePath == _file),
            Arg.Any<IProgress<long>?>(),
            Arg.Any<CancellationToken>());
        await _api.Received().AddExamDependencyAsync(
            ExamId, "exams/k/dependencies/abc", "GCC", "14.2.0", DependencyPlatform.LinuxX64, "token-value",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddToolchain_Should_NotCommit_WhenTheUploadFails()
    {
        // Arrange
        _api.CreateDependencyUploadUrlAsync(ExamId, "token-value", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new DependencyUploadTarget(
                "exams/k/dependencies/abc", new Uri("http://localhost:9000/d.zip"), DateTimeOffset.UtcNow)));
        _uploader.UploadAsync(Arg.Any<UploadRequest>(), Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure(new ApiError(403, "Upload.LinkExpired", "The upload link has expired.", [])));

        // Act
        ApiResult result = await _service.AddToolchainAsync(ExamId, _file, "GCC", "14.2.0", DependencyPlatform.Any);

        // Assert
        result.Error!.Code.ShouldBe("Upload.LinkExpired");
        await _api.DidNotReceive().AddExamDependencyAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DependencyPlatform>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // The server reads the stored size back at commit and deletes anything over the cap, so the
    // refusal has to reach the screen rather than being mistaken for success.
    [Fact]
    public async Task AddToolchain_Should_ReportTheRefusal_WhenTheArchiveIsTooLarge()
    {
        // Arrange
        _api.CreateDependencyUploadUrlAsync(ExamId, "token-value", Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success(new DependencyUploadTarget(
                "exams/k/dependencies/abc", new Uri("http://localhost:9000/d.zip"), DateTimeOffset.UtcNow)));
        _uploader.UploadAsync(Arg.Any<UploadRequest>(), Arg.Any<IProgress<long>?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success());
        _api.AddExamDependencyAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DependencyPlatform>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<Guid>(new ApiError(400, "Exams.DependencyTooLarge", "Too large.", [])));

        // Act
        ApiResult result = await _service.AddToolchainAsync(ExamId, _file, "GCC", "14.2.0", DependencyPlatform.Any);

        // Assert
        result.Error!.Code.ShouldBe("Exams.DependencyTooLarge");
    }

    [Fact]
    public async Task Adding_Should_Stop_WhenThereIsNoValidToken()
    {
        // Arrange
        _session.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failure<string>(ApiError.Unreachable("Cannot reach the server.")));

        // Act
        ApiResult file = await _service.AddTaskFileAsync(ExamId, _file, "tasks.pdf");
        ApiResult toolchain = await _service.AddToolchainAsync(ExamId, _file, "GCC", "1", DependencyPlatform.Any);

        // Assert
        file.Error!.IsUnreachable.ShouldBeTrue();
        toolchain.Error!.IsUnreachable.ShouldBeTrue();
        await _api.DidNotReceive().CreateDependencyUploadUrlAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
