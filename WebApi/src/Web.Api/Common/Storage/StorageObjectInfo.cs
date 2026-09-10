namespace Web.Api.Common.Storage;

// What the object store itself reports about a stored object. Metadata written to the database is
// taken from here rather than from the request that claims to have uploaded it.
//
// Sha256 is the digest the server recorded when it wrote the object, and null for anything written
// without one - a dependency uploaded straight to storage on a presigned URL, for instance.
public sealed record StorageObjectInfo(
    string ObjectKey,
    long SizeBytes,
    string ContentType,
    string? Sha256 = null);
