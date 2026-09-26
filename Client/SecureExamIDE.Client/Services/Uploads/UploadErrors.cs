using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Uploads;

internal static class UploadErrors
{
    // Status 0, like an unreachable server: nothing was committed, so starting again is safe.
    public static readonly ApiError Interrupted = new(
        0,
        "Upload.Interrupted",
        "The upload was interrupted. Start it again.",
        []);

    // Storage refuses a presigned link once it has expired; asking the API again gives a new one.
    public static readonly ApiError LinkExpired = new(
        403,
        "Upload.LinkExpired",
        "The upload link has expired. Start the upload again.",
        []);

    public static ApiError Failed(int statusCode) => new(
        statusCode,
        "Upload.Failed",
        "The file could not be uploaded. Try again later.",
        []);

    public static ApiError DiskError(string detail) => new(
        0,
        "Upload.DiskError",
        $"The file could not be read from this computer: {detail}",
        []);
}
