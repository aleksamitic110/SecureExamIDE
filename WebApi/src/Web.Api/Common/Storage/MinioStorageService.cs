using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Web.Api.Common.Storage;

public sealed class MinioStorageService : IStorageService, IDisposable
{
    private readonly IMinioClient _minioClient;

    // Presigned URLs are handed to a client, so they must be signed for the address that client
    // can actually reach. When PublicEndpoint is unset this is the same instance as _minioClient.
    private readonly IMinioClient _presigningClient;

    private readonly string _bucketName;

    public MinioStorageService(IOptions<StorageOptions> options)
    {
        StorageOptions storageOptions = options.Value;

        // The client is owned by this field for the lifetime of the service and released in
        // Dispose(), which the DI container calls when the scope ends. The analyzer cannot see
        // ownership transfer through the builder chain.
        _minioClient = Build(storageOptions, storageOptions.Endpoint);

        _presigningClient = string.IsNullOrWhiteSpace(storageOptions.PublicEndpoint)
            ? _minioClient
            : Build(storageOptions, storageOptions.PublicEndpoint);

        _bucketName = storageOptions.Bucket;
    }

    public void Dispose()
    {
        if (!ReferenceEquals(_presigningClient, _minioClient))
        {
            _presigningClient.Dispose();
        }

        _minioClient.Dispose();
    }

    // The clients are owned by this instance for its lifetime and released in Dispose(), which the
    // DI container calls when the scope ends. The analyzer cannot see ownership transfer through
    // the builder chain.
#pragma warning disable CA2000
    private static IMinioClient Build(StorageOptions options, string endpoint) =>
        new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(options.AccessKey, options.SecretKey)
            .WithSSL(options.UseSsl)
            .Build();
#pragma warning restore CA2000

    public async Task PutAsync(
        string objectKey,
        Stream content,
        string contentType,
        string? sha256,
        CancellationToken cancellationToken = default)
    {
        var args = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectKey)
            .WithStreamData(content)
            .WithObjectSize(content.Length)
            .WithContentType(contentType);

        // User metadata is written in the same request as the bytes, so an object and its digest
        // can never exist apart - and nothing but this server can write it, since phase-one keys
        // are never handed out as presigned upload URLs.
        if (sha256 is not null)
        {
            args = args.WithHeaders(new Dictionary<string, string> { [Sha256MetadataHeader] = sha256 });
        }

        await _minioClient.PutObjectAsync(args, cancellationToken);
    }

    public async Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();
        
        var args = new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectKey)
            .WithCallbackStream(stream =>
            {
                stream.CopyTo(memoryStream);
                memoryStream.Position = 0;
            });

        await _minioClient.GetObjectAsync(args, cancellationToken);
        
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task<StorageObjectInfo?> StatAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var args = new StatObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectKey);

        try
        {
            ObjectStat stat = await _minioClient.StatObjectAsync(args, cancellationToken);

            // The SDK strips the x-amz-meta- prefix from user metadata and compares keys without
            // regard to case, so the digest written under x-amz-meta-sha256 comes back as "sha256".
            string? sha256 = stat.MetaData.TryGetValue(Sha256MetadataKey, out string? value) ? value : null;

            return new StorageObjectInfo(objectKey, stat.Size, stat.ContentType, sha256);
        }
        catch (ObjectNotFoundException)
        {
            // A missing object is an ordinary answer to "is this there?", not a fault.
            return null;
        }
        catch (BucketNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var args = new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectKey);

        await _minioClient.RemoveObjectAsync(args, cancellationToken);
    }

    public async Task<string> CreatePresignedUploadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default)
    {
        var args = new PresignedPutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectKey)
            .WithExpiry((int)expiresIn.TotalSeconds);

        return await _presigningClient.PresignedPutObjectAsync(args);
    }

    public async Task<string> CreatePresignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectKey)
            .WithExpiry((int)expiresIn.TotalSeconds);

        return await _presigningClient.PresignedGetObjectAsync(args);
    }

    private const string Sha256MetadataKey = "sha256";
    private const string Sha256MetadataHeader = "x-amz-meta-" + Sha256MetadataKey;
}