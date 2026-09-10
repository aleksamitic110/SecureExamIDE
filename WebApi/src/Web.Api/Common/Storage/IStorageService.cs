namespace Web.Api.Common.Storage;

public interface IStorageService
{
    // sha256, when given, is recorded as metadata on the object itself and handed back by
    // StatAsync. That is how a commit learns the digest the server measured at upload without
    // taking the caller's word for it, and without reading the whole object back.
    Task PutAsync(
        string objectKey,
        Stream content,
        string contentType,
        string? sha256,
        CancellationToken cancellationToken = default);

    Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken = default);

    // Reports what the store holds for objectKey, or null if there is no
    // such object. This is what lets a metadata row be written only after the bytes really exist.
    Task<StorageObjectInfo?> StatAsync(string objectKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
    Task<string> CreatePresignedUploadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default);
    Task<string> CreatePresignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default);
}
