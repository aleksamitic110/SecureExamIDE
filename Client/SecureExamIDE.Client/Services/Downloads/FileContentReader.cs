using System.Security.Cryptography;
using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Downloads;

internal sealed class FileContentReader(HttpClient httpClient) : IFileContentReader
{
    public async Task<ApiResult<byte[]>> ReadAsync(
        Uri url,
        long? expectedSizeBytes = null,
        string? expectedSha256 = null,
        CancellationToken cancellationToken = default)
    {
        byte[] content;

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // A presigned link that has run out is the likely case, and the professor can simply
                // open the submission again.
                return ApiResult.Failure<byte[]>(DownloadErrors.LinkExpired);
            }

            content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ApiResult.Failure<byte[]>(DownloadErrors.Interrupted);
        }

        if (expectedSizeBytes is { } size && content.LongLength != size)
        {
            return ApiResult.Failure<byte[]>(DownloadErrors.Corrupted);
        }

        if (expectedSha256 is null)
        {
            return ApiResult.Success(content);
        }

        // The digest the server measured when the work arrived. Matching it is what makes this the
        // very file the student handed in, rather than something storage returned since.
        string digest = Convert.ToHexStringLower(SHA256.HashData(content));

        return string.Equals(digest, expectedSha256, StringComparison.OrdinalIgnoreCase)
            ? ApiResult.Success(content)
            : ApiResult.Failure<byte[]>(DownloadErrors.Corrupted);
    }
}
