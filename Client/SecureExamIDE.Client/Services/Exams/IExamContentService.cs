using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Exams;

// Adding content to a draft exam. Both kinds go in two phases - the bytes first, the metadata row
// only once the server can see them - but by different routes, and this is where that difference
// lives so the screen does not have to know about it.
public interface IExamContentService
{
    // A task file is proxied through the API, which is what makes the recorded digest the server's
    // own measurement rather than a claim about the professor's own file.
    Task<ApiResult> AddTaskFileAsync(
        Guid examId,
        string sourcePath,
        string fileName,
        CancellationToken cancellationToken = default);

    // A toolchain goes straight into storage through a presigned link, because it is hundreds of
    // megabytes of public archive. It carries no digest, deliberately: the API never sees the bytes.
    Task<ApiResult> AddToolchainAsync(
        Guid examId,
        string sourcePath,
        string name,
        string version,
        DependencyPlatform platform,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);
}
