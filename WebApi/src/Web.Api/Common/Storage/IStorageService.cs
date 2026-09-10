namespace Web.Api.Common.Storage;

public interface IStorageService
{
    Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken = default);

    // Reports what the store holds for objectKey, or null if there is no
    // such object. This is what lets a metadata row be written only after the bytes really exist.
    Task<StorageObjectInfo?> StatAsync(string objectKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
    Task<string> CreatePresignedUploadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default);
    Task<string> CreatePresignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default);
}
