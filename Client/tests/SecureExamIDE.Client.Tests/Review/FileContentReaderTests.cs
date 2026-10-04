using System.Net;
using System.Security.Cryptography;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Downloads;

namespace SecureExamIDE.Client.Tests.Review;

// Reading a submission into memory, checked against what the server measured when the work arrived.
// That check is what lets a professor say the file is the one that was handed in.
public sealed class FileContentReaderTests : IDisposable
{
    private static readonly byte[] Content = "the sealed solution"u8.ToArray();

    private readonly StubHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly FileContentReader _reader;

    public FileContentReaderTests()
    {
        _httpClient = new HttpClient(_handler);
        _reader = new FileContentReader(_httpClient);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string DigestOf(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static readonly Uri Url = new("http://storage.test/submission.bin");

    [Fact]
    public async Task Read_Should_ReturnTheBytes_WhenTheyMatchWhatTheServerRecorded()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK, Content);

        // Act
        ApiResult<byte[]> result = await _reader.ReadAsync(Url, Content.LongLength, DigestOf(Content));

        // Assert
        result.Value.ShouldBe(Content);
    }

    [Fact]
    public async Task Read_Should_RefuseBytesThatDoNotMatchTheDigest()
    {
        // Arrange - storage returned something else than what arrived with the submission.
        _handler.Respond(HttpStatusCode.OK, "a different solution"u8.ToArray());

        // Act
        ApiResult<byte[]> result = await _reader.ReadAsync(Url, "a different solution"u8.ToArray().LongLength, DigestOf(Content));

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("Download.Corrupted");
    }

    [Fact]
    public async Task Read_Should_RefuseTheWrongNumberOfBytes()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK, Content);

        // Act
        ApiResult<byte[]> result = await _reader.ReadAsync(Url, Content.LongLength + 10, DigestOf(Content));

        // Assert
        result.Error!.Code.ShouldBe("Download.Corrupted");
    }

    [Fact]
    public async Task Read_Should_SayWhenTheLinkHasExpired()
    {
        // Arrange - storage refuses a presigned link once it has run out.
        _handler.Respond(HttpStatusCode.Forbidden, []);

        // Act
        ApiResult<byte[]> result = await _reader.ReadAsync(Url, Content.LongLength, DigestOf(Content));

        // Assert
        result.Error!.Code.ShouldBe("Download.LinkExpired");
    }

    // A sitting's package header has no recorded size or digest, and needs none.
    [Fact]
    public async Task Read_Should_AcceptAnythingWhenNothingWasRecorded()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK, Content);

        // Act
        ApiResult<byte[]> result = await _reader.ReadAsync(Url);

        // Assert
        result.Value.ShouldBe(Content);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private byte[] _content = [];

        public void Respond(HttpStatusCode statusCode, byte[] content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new ByteArrayContent(_content) });
    }
}
