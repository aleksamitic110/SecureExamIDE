using System.Net;
using System.Net.Http.Headers;
using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Uploads;

// Uploads to presigned storage links. Like the downloader, these go straight to object storage
// rather than through the API, so they carry no token and use an HttpClient without a timeout: a
// toolchain of several hundred megabytes on a home connection takes far longer than any API call
// should be allowed to.
//
// There is no resuming here, unlike a download. A presigned PUT writes the object in one request,
// so an interrupted upload simply starts again; what it leaves behind is an object no row refers
// to, which is harmless.
internal sealed class FileUploader(HttpClient httpClient) : IFileUploader, IDisposable
{
    public async Task<ApiResult> UploadAsync(
        UploadRequest request,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using FileStream source = File.OpenRead(request.SourcePath);
            await using var counted = new CountingStream(source, progress);
            using var content = new StreamContent(counted);

            content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);

            // Storage needs the length up front; without it HttpClient would send the body chunked,
            // which a presigned PUT does not accept.
            content.Headers.ContentLength = source.Length;

            using var message = new HttpRequestMessage(HttpMethod.Put, request.UploadUrl) { Content = content };
            using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ApiResult.Success();
            }

            return ApiResult.Failure(response.StatusCode == HttpStatusCode.Forbidden
                ? UploadErrors.LinkExpired
                : UploadErrors.Failed((int)response.StatusCode));
        }
        // The three ways opening the file itself fails. They come first because two of them are
        // IOExceptions, and a file that is not there is a different problem from a dropped connection.
        catch (FileNotFoundException exception)
        {
            return ApiResult.Failure(UploadErrors.DiskError(exception.Message));
        }
        catch (DirectoryNotFoundException exception)
        {
            return ApiResult.Failure(UploadErrors.DiskError(exception.Message));
        }
        catch (UnauthorizedAccessException exception)
        {
            return ApiResult.Failure(UploadErrors.DiskError(exception.Message));
        }
        catch (HttpRequestException)
        {
            return ApiResult.Failure(UploadErrors.Interrupted);
        }
        catch (IOException)
        {
            return ApiResult.Failure(UploadErrors.Interrupted);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiResult.Failure(UploadErrors.Interrupted);
        }
    }

    public void Dispose() => httpClient.Dispose();

    // Wraps the file being sent so the screen can show progress on a long upload. Read-only and
    // forward-only, which is all HttpClient asks of a request body.
    private sealed class CountingStream(Stream inner, IProgress<long>? progress) : Stream
    {
        private long _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => _sent;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Report(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            Report(await inner.ReadAsync(buffer, cancellationToken));

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Report(int read)
        {
            if (read > 0)
            {
                _sent += read;
                progress?.Report(_sent);
            }

            return read;
        }
    }
}
