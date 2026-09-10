namespace Web.Api.Common.Storage;

// What the object store itself reports about a stored object. Metadata written to the database is
// taken from here rather than from the request that claims to have uploaded it.
public sealed record StorageObjectInfo(string ObjectKey, long SizeBytes, string ContentType);
