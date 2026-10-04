using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Downloads;

// Reads a presigned link straight into memory, checking what arrived against what the server said it
// measured. The student's downloads go to disk through IFileDownloader because a toolchain is hundreds
// of megabytes; a submission is small, and a professor reviewing one should leave nothing behind.
public interface IFileContentReader
{
    // Size and digest are what the server recorded. Both are optional: a sitting's package header has
    // neither and needs neither, because AES-GCM refuses to unwrap an altered one.
    Task<ApiResult<byte[]>> ReadAsync(
        Uri url,
        long? expectedSizeBytes = null,
        string? expectedSha256 = null,
        CancellationToken cancellationToken = default);
}
