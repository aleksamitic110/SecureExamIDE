namespace SecureExamIDE.Client.Services.Downloads;

// What to fetch and how to know it arrived intact. The size is always known for exam material;
// the digest only for the sealed package, since dependencies carry none.
public sealed record DownloadRequest(
    Uri Url,
    string DestinationPath,
    long? ExpectedSizeBytes,
    string? ExpectedSha256);
