namespace SecureExamIDE.Client.Services.Uploads;

// One file to put into object storage through a presigned link.
public sealed record UploadRequest(Uri UploadUrl, string SourcePath, string ContentType);
