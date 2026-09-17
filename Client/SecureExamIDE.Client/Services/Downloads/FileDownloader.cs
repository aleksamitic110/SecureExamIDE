using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Downloads;

// Downloads from presigned storage links. These go straight to object storage rather than through
// the API, so they carry no token, and they use their own HttpClient without a timeout: a
// multi-gigabyte toolchain on a home connection can take far longer than any API call should.
//
// A download is written to "<name>.part" and resumed with an HTTP Range request when that file
// already exists - the reason a dropped connection halfway through a compiler does not mean
// starting again from zero.
internal sealed class FileDownloader(HttpClient httpClient) : IFileDownloader, IDisposable
{
    public async Task<ApiResult> DownloadAsync(
        DownloadRequest request,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string partPath = request.DestinationPath + PartSuffix;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);

            ApiResult fetched = await FetchAsync(request, partPath, progress, allowResume: true, cancellationToken);

            if (!fetched.IsSuccess)
            {
                return fetched;
            }

            if (!await IsIntactAsync(request, partPath, cancellationToken))
            {
                File.Delete(partPath);
                return ApiResult.Failure(DownloadErrors.Corrupted);
            }

            File.Move(partPath, request.DestinationPath, overwrite: true);

            return ApiResult.Success();
        }
        catch (IOException exception)
        {
            return ApiResult.Failure(DownloadErrors.DiskError(exception.Message));
        }
        catch (UnauthorizedAccessException exception)
        {
            return ApiResult.Failure(DownloadErrors.DiskError(exception.Message));
        }
    }

    public void Dispose() => httpClient.Dispose();

    private async Task<ApiResult> FetchAsync(
        DownloadRequest request,
        string partPath,
        IProgress<long>? progress,
        bool allowResume,
        CancellationToken cancellationToken)
    {
        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        // Longer than the file can be means it is not a prefix of this file at all.
        if (existing > 0 && request.ExpectedSizeBytes is { } expected && existing > expected)
        {
            File.Delete(partPath);
            existing = 0;
        }

        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);

        if (existing > 0)
        {
            message.Headers.Range = new RangeHeaderValue(existing, null);
        }

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ApiResult.Failure(DownloadErrors.Interrupted);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.PartialContent when existing > 0:
                    break;

                case HttpStatusCode.OK:
                    // The server sent the whole file: whatever was there before is replaced.
                    existing = 0;
                    break;

                // Everything was already on disk; the size and digest checks decide the rest.
                case HttpStatusCode.RequestedRangeNotSatisfiable when existing > 0 && existing == request.ExpectedSizeBytes:
                    return ApiResult.Success();

                case HttpStatusCode.RequestedRangeNotSatisfiable when allowResume:
                    File.Delete(partPath);
                    return await FetchAsync(request, partPath, progress, allowResume: false, cancellationToken);

                case HttpStatusCode.Forbidden:
                    return ApiResult.Failure(DownloadErrors.LinkExpired);

                default:
                    return ApiResult.Failure(DownloadErrors.Failed((int)response.StatusCode));
            }

            progress?.Report(existing);

            try
            {
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(
                    partPath,
                    existing > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    useAsync: true);

                await CopyAsync(source, target, existing, progress, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return ApiResult.Failure(DownloadErrors.Interrupted);
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested && File.Exists(partPath))
            {
                // A connection reset surfaces as an IOException from the response stream. What was
                // written stays, so the next attempt resumes from it.
                return ApiResult.Failure(DownloadErrors.Interrupted);
            }

            return ApiResult.Success();
        }
    }

    private static async Task CopyAsync(
        Stream source,
        FileStream target,
        long alreadyOnDisk,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[BufferSize];
        long written = alreadyOnDisk;
        long lastReported = written;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;

            // Reporting every buffer would flood the UI thread on a fast connection.
            if (written - lastReported >= ReportEveryBytes)
            {
                progress?.Report(written);
                lastReported = written;
            }
        }

        progress?.Report(written);
    }

    private static async Task<bool> IsIntactAsync(DownloadRequest request, string partPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(partPath))
        {
            return false;
        }

        if (request.ExpectedSizeBytes is { } expected && new FileInfo(partPath).Length != expected)
        {
            return false;
        }

        if (request.ExpectedSha256 is null)
        {
            return true;
        }

        await using FileStream file = File.OpenRead(partPath);
        byte[] digest = await SHA256.HashDataAsync(file, cancellationToken);

        return string.Equals(Convert.ToHexStringLower(digest), request.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private const string PartSuffix = ".part";
    private const int BufferSize = 81_920;
    private const long ReportEveryBytes = 256 * 1024;
}
