using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Downloads;

namespace SecureExamIDE.Client.Tests.Downloads;

public sealed class FileDownloaderTests : IDisposable
{
    private static readonly Uri Link = new("http://storage.test/exam-packages/package.bin?X-Amz-Signature=abc");
    private static readonly byte[] Content = [.. Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251))];
    private static readonly string ContentSha256 = Convert.ToHexStringLower(SHA256.HashData(Content));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly StorageStub _storage = new();

    private string Destination => Path.Combine(_directory, "nested", "package.bin");

#pragma warning disable CA2000 // The downloader owns the HttpClient and disposes it.
    private FileDownloader CreateDownloader() => new(new HttpClient(_storage, disposeHandler: false));
#pragma warning restore CA2000

    public void Dispose()
    {
        _storage.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_Should_SaveTheFile_WhenSizeAndDigestMatch()
    {
        // Arrange
        using FileDownloader downloader = CreateDownloader();
        long lastReported = 0;

        // Act
        ApiResult result = await downloader.DownloadAsync(
            new DownloadRequest(Link, Destination, Content.Length, ContentSha256),
            new SynchronousProgress(bytes => lastReported = bytes));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await File.ReadAllBytesAsync(Destination)).ShouldBe(Content);
        File.Exists(Destination + ".part").ShouldBeFalse();
        lastReported.ShouldBe(Content.Length);
    }

    // A package that is not the one the sitting was listed with must never be kept.
    [Fact]
    public async Task Download_Should_DiscardTheFile_WhenTheDigestDoesNotMatch()
    {
        // Arrange
        using FileDownloader downloader = CreateDownloader();

        // Act
        ApiResult result = await downloader.DownloadAsync(
            new DownloadRequest(Link, Destination, Content.Length, new string('0', 64)));

        // Assert
        result.Error!.Code.ShouldBe("Download.Corrupted");
        File.Exists(Destination).ShouldBeFalse();
        File.Exists(Destination + ".part").ShouldBeFalse();
    }

    [Fact]
    public async Task Download_Should_DiscardTheFile_WhenItIsShorterThanRecorded()
    {
        // Arrange
        using FileDownloader downloader = CreateDownloader();

        // Act
        ApiResult result = await downloader.DownloadAsync(new DownloadRequest(Link, Destination, Content.Length + 1, null));

        // Assert
        result.Error!.Code.ShouldBe("Download.Corrupted");
        File.Exists(Destination).ShouldBeFalse();
    }

    // The point of the .part file: a dropped connection halfway through does not start over.
    [Fact]
    public async Task Download_Should_ResumeFromWhatIsAlreadyOnDisk()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await File.WriteAllBytesAsync(Destination + ".part", Content[..100_000]);
        using FileDownloader downloader = CreateDownloader();

        // Act
        ApiResult result = await downloader.DownloadAsync(new DownloadRequest(Link, Destination, Content.Length, ContentSha256));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _storage.RangesRequested.ShouldHaveSingleItem().ShouldBe(100_000);
        _storage.BytesSent.ShouldBe(Content.Length - 100_000);
        (await File.ReadAllBytesAsync(Destination)).ShouldBe(Content);
    }

    [Fact]
    public async Task Download_Should_StartOver_WhenTheServerIgnoresTheRange()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await File.WriteAllBytesAsync(Destination + ".part", Content[..100_000]);
        _storage.SupportsRanges = false;
        using FileDownloader downloader = CreateDownloader();

        // Act
        ApiResult result = await downloader.DownloadAsync(new DownloadRequest(Link, Destination, Content.Length, ContentSha256));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await File.ReadAllBytesAsync(Destination)).ShouldBe(Content);
    }

    [Fact]
    public async Task Download_Should_KeepWhatArrived_WhenTheConnectionDrops()
    {
        // Arrange
        _storage.DropAfterBytes = 120_000;
        using FileDownloader downloader = CreateDownloader();

        // Act
        ApiResult first = await downloader.DownloadAsync(new DownloadRequest(Link, Destination, Content.Length, ContentSha256));
        _storage.DropAfterBytes = null;
        ApiResult second = await downloader.DownloadAsync(new DownloadRequest(Link, Destination, Content.Length, ContentSha256));

        // Assert
        first.Error!.Code.ShouldBe("Download.Interrupted");
        second.IsSuccess.ShouldBeTrue();
        _storage.RangesRequested.ShouldHaveSingleItem().ShouldBeGreaterThan(0);
        (await File.ReadAllBytesAsync(Destination)).ShouldBe(Content);
    }

    [Fact]
    public async Task Download_Should_ExplainAnExpiredLink()
    {
        // Arrange
        _storage.Status = HttpStatusCode.Forbidden;
        using FileDownloader downloader = CreateDownloader();

        // Act
        ApiResult result = await downloader.DownloadAsync(new DownloadRequest(Link, Destination, Content.Length, null));

        // Assert
        result.Error!.Code.ShouldBe("Download.LinkExpired");
        File.Exists(Destination).ShouldBeFalse();
    }

    // Serves Content like object storage does: Range requests answered with 206 and the remainder.
    private sealed class StorageStub : HttpMessageHandler
    {
        public bool SupportsRanges { get; set; } = true;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public int? DropAfterBytes { get; set; }

        public List<long> RangesRequested { get; } = [];

        public long BytesSent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Status != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(Status));
            }

            long from = 0;

            if (request.Headers.Range?.Ranges.SingleOrDefault()?.From is { } start)
            {
                RangesRequested.Add(start);
                from = SupportsRanges ? start : 0;
            }

            byte[] body = Content[(int)from..];
            BytesSent += DropAfterBytes is null ? body.Length : Math.Min(body.Length, DropAfterBytes.Value);

            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(new DroppingStream(body, DropAfterBytes))
            };

            response.Content.Headers.ContentLength = body.Length;

            if (from > 0)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, Content.Length - 1, Content.Length);
            }

            return Task.FromResult(response);
        }
    }

    // A response body that fails part-way, the way a reset connection does.
    private sealed class DroppingStream(byte[] body, int? dropAfter) : MemoryStream(body)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (dropAfter is { } limit && Position >= limit)
            {
                throw new IOException("The connection was reset.");
            }

            int allowed = dropAfter is { } cap ? (int)Math.Min(buffer.Length, cap - Position) : buffer.Length;

            return await base.ReadAsync(buffer[..allowed], cancellationToken);
        }
    }

    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
