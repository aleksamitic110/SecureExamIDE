using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Uploads;

public interface IFileUploader
{
    // Streams the file straight into object storage and reports how many of its bytes have gone.
    // Nothing is recorded anywhere until the API is told about the object afterwards, so an upload
    // that fails half-way leaves an object nobody refers to rather than a broken exam.
    Task<ApiResult> UploadAsync(
        UploadRequest request,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);
}
