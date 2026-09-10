namespace Web.Api.Common.Storage;

public sealed class StorageOptions
{
    public required string Endpoint { get; set; }
    public required string AccessKey { get; set; }
    public required string SecretKey { get; set; }
    public required string Bucket { get; set; }
    public bool UseSsl { get; set; }

    // The address a *client* can reach the store at, used only when signing download URLs. Inside
    // Docker the API talks to "minio:9000", which means nothing on a student's laptop - and the
    // host name cannot simply be swapped afterwards, because S3 signatures cover the Host header.
    // Left unset when the API and its clients see the store at the same address.
    public string? PublicEndpoint { get; set; }
}
