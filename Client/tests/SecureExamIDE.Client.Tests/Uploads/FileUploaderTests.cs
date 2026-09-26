using System.Net;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Uploads;
using SecureExamIDE.Client.Tests.Fakes;

namespace SecureExamIDE.Client.Tests.Uploads;

public sealed class FileUploaderTests : IDisposable
{
    private readonly StubHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly FileUploader _uploader;
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"sei-{Guid.NewGuid():N}.bin");

    public FileUploaderTests()
    {
        _httpClient = new HttpClient(_handler, disposeHandler: false);
        _uploader = new FileUploader(_httpClient);
        File.WriteAllBytes(_file, new byte[4096]);
    }

    public void Dispose()
    {
        _uploader.Dispose();
        _handler.Dispose();
        File.Delete(_file);
    }

    private UploadRequest Request() =>
        new(new Uri("http://localhost:9000/exam-packages/d.zip?X-Amz-Signature=abc"), _file, "application/zip");

    // A presigned PUT will not take a chunked body, so the length has to go with the request.
    [Fact]
    public async Task Upload_Should_PutTheFileToTheLink()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK);

        // Act
        ApiResult result = await _uploader.UploadAsync(Request());

        // Assert
        result.IsSuccess.ShouldBeTrue();

        StubHttpMessageHandler.RecordedRequest request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Put);
        request.Path.ShouldBe("/exam-packages/d.zip");
        request.Authorization.ShouldBeNull("a presigned link carries its own signature, never a token");
    }

    [Fact]
    public async Task Upload_Should_ReportProgress_AsTheBytesGo()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK);
        List<long> reported = [];

        // Act
        await _uploader.UploadAsync(Request(), new Progress<long>(reported.Add));

        // The progress callback is posted to the synchronisation context, so give it a moment.
        await Task.Delay(50);

        // Assert
        reported.ShouldNotBeEmpty();
        reported[^1].ShouldBe(4096);
    }

    // Storage refuses an expired link with 403, and asking the API again gives a new one.
    [Fact]
    public async Task Upload_Should_SayTheLinkExpired_OnForbidden()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.Forbidden);

        // Act
        ApiResult result = await _uploader.UploadAsync(Request());

        // Assert
        result.Error!.Code.ShouldBe("Upload.LinkExpired");
    }

    [Fact]
    public async Task Upload_Should_ReportAFailure_OnAnyOtherStatus()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.InternalServerError);

        // Act
        ApiResult result = await _uploader.UploadAsync(Request());

        // Assert
        result.Error!.Code.ShouldBe("Upload.Failed");
        result.Error.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task Upload_Should_ReportADiskProblem_WhenTheFileIsGone()
    {
        // Act
        ApiResult result = await _uploader.UploadAsync(
            Request() with { SourcePath = Path.Combine(Path.GetTempPath(), "no-such-file") });

        // Assert
        result.Error!.Code.ShouldBe("Upload.DiskError");
    }

    // A dropped connection leaves an object nobody refers to, never a half-recorded dependency.
    [Fact]
    public async Task Upload_Should_SayItWasInterrupted_WhenTheConnectionDrops()
    {
        // Arrange
        _handler.Fail(new HttpRequestException("connection reset"));

        // Act
        ApiResult result = await _uploader.UploadAsync(Request());

        // Assert
        result.Error!.Code.ShouldBe("Upload.Interrupted");
        result.Error.IsUnreachable.ShouldBeTrue();
    }
}
