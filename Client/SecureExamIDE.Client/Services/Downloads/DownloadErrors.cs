using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Downloads;

internal static class DownloadErrors
{
    // Status 0, like an unreachable server: the partial file is kept, so trying again continues
    // where the connection dropped.
    public static readonly ApiError Interrupted = new(
        0,
        "Download.Interrupted",
        "The download was interrupted. Start it again to continue where it stopped.",
        []);

    // Storage refuses a presigned link once it has expired; asking the API again gives a new one.
    public static readonly ApiError LinkExpired = new(
        403,
        "Download.LinkExpired",
        "The download link has expired. Start the download again.",
        []);

    public static readonly ApiError Corrupted = new(
        0,
        "Download.Corrupted",
        "A downloaded file did not match what the server recorded, so it was discarded. Download it again.",
        []);

    public static ApiError Failed(int statusCode) => new(
        statusCode,
        "Download.Failed",
        "The file could not be downloaded. Try again later.",
        []);

    public static ApiError DiskError(string detail) => new(
        0,
        "Download.DiskError",
        $"The file could not be saved on this computer: {detail}",
        []);
}
