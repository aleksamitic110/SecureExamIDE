using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Downloads;

public interface IFileDownloader
{
    // Streams the file to disk and reports how many bytes of it are on disk so far. The file only
    // appears under its final name once it is complete and has passed the size and digest checks.
    // Cancelling keeps what has arrived, so the next attempt resumes.
    Task<ApiResult> DownloadAsync(
        DownloadRequest request,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);
}
