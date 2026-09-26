using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Uploads;

namespace SecureExamIDE.Client.Services.Exams;

internal sealed class ExamContentService(
    IApiClient apiClient,
    ISessionService session,
    IFileUploader uploader) : IExamContentService
{
    public async Task<ApiResult> AddTaskFileAsync(
        Guid examId,
        string sourcePath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        if (!token.IsSuccess)
        {
            return ApiResult.Failure(token.Error);
        }

        ApiResult<UploadedExamFile> uploaded;

        try
        {
            await using FileStream content = File.OpenRead(sourcePath);

            uploaded = await apiClient.UploadExamFileContentAsync(
                examId, fileName, content, ContentTypeOf(fileName), token.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ApiResult.Failure(UploadErrors.DiskError(exception.Message));
        }

        if (!uploaded.IsSuccess)
        {
            return ApiResult.Failure(uploaded.Error);
        }

        // A fresh token for the commit: the upload may have taken a while.
        ApiResult<string> commitToken = await session.GetAccessTokenAsync(cancellationToken);

        if (!commitToken.IsSuccess)
        {
            return ApiResult.Failure(commitToken.Error);
        }

        ApiResult<Guid> committed = await apiClient.AddExamFileAsync(
            examId, uploaded.Value.ObjectKey, fileName, commitToken.Value, cancellationToken);

        return committed.IsSuccess ? ApiResult.Success() : ApiResult.Failure(committed.Error);
    }

    public async Task<ApiResult> AddToolchainAsync(
        Guid examId,
        string sourcePath,
        string name,
        string version,
        DependencyPlatform platform,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        if (!token.IsSuccess)
        {
            return ApiResult.Failure(token.Error);
        }

        ApiResult<DependencyUploadTarget> target = await apiClient.CreateDependencyUploadUrlAsync(
            examId, token.Value, cancellationToken);

        if (!target.IsSuccess)
        {
            return ApiResult.Failure(target.Error);
        }

        ApiResult uploaded = await uploader.UploadAsync(
            new UploadRequest(target.Value.UploadUrl, sourcePath, ContentTypeOf(sourcePath)),
            progress,
            cancellationToken);

        if (!uploaded.IsSuccess)
        {
            return uploaded;
        }

        // The upload can take minutes, so the token for the commit is asked for again.
        ApiResult<string> commitToken = await session.GetAccessTokenAsync(cancellationToken);

        if (!commitToken.IsSuccess)
        {
            return ApiResult.Failure(commitToken.Error);
        }

        // The server reads the size back from storage and refuses - and deletes - anything over the
        // cap, so an oversized archive is reported here rather than silently recorded.
        ApiResult<Guid> committed = await apiClient.AddExamDependencyAsync(
            examId, target.Value.ObjectKey, name, version, platform, commitToken.Value, cancellationToken);

        return committed.IsSuccess ? ApiResult.Success() : ApiResult.Failure(committed.Error);
    }

    // Enough to let a student's client tell a PDF from a text file and an archive from either. The
    // server reads the type back from storage for a dependency, so this is only what is sent.
    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToUpperInvariant() switch
    {
        ".PDF" => "application/pdf",
        ".TXT" => "text/plain",
        ".MD" => "text/markdown",
        ".ZIP" => "application/zip",
        ".GZ" or ".TGZ" => "application/gzip",
        ".C" or ".H" or ".CPP" or ".HPP" => "text/plain",
        _ => "application/octet-stream"
    };
}
