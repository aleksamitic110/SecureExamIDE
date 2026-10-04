using System.Security.Cryptography;
using System.Text;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Downloads;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Unlock;

namespace SecureExamIDE.Client.Services.Review;

// Opening a submission is the mirror of sealing it, and it needs nothing from the student's computer:
//
//   package header  <- GET /sessions/{id}/package        (the professor's own request)
//   content key     <- Argon2id(one-time code, header)   the same derivation the student ran
//   hand-in key     <- HKDF(content key)                 no machine id, which is the point
//   solution        <- AES-256-GCM-open(hand-in key, submission.bin), then a zip
//   activity log    <- the same key, line by line, with its chain checked
internal sealed class SubmissionReview(
    IApiClient apiClient,
    ISessionService session,
    IFileContentReader content) : ISubmissionReview
{
    public async Task<ApiResult<PagedList<SubmissionSummary>>> GetSubmissionsAsync(
        Guid sittingId,
        int page,
        CancellationToken cancellationToken = default)
    {
        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        return token.IsSuccess
            ? await apiClient.GetSessionSubmissionsAsync(sittingId, page, PageSize, token.Value, cancellationToken)
            : ApiResult.Failure<PagedList<SubmissionSummary>>(token.Error);
    }

    public async Task<ApiResult<byte[]>> DeriveHandInKeyAsync(
        Guid sittingId,
        string typedCode,
        CancellationToken cancellationToken = default)
    {
        string code = OneTimeCode.Normalize(typedCode);

        if (code.Length != OneTimeCode.Length)
        {
            return ApiResult.Failure<byte[]>(UnlockErrors.IncompleteCode);
        }

        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        if (!token.IsSuccess)
        {
            return ApiResult.Failure<byte[]>(token.Error);
        }

        // The header is public - it is what makes unlocking possible offline - and the professor may
        // read the sitting's package like any signed-in user.
        ApiResult<SittingPackage> links = await apiClient.GetSittingPackageAsync(sittingId, token.Value, cancellationToken);

        if (!links.IsSuccess)
        {
            return ApiResult.Failure<byte[]>(links.Error);
        }

        // The header has no recorded size or digest, and needs none: AES-GCM refuses to unwrap an
        // altered one, which is what a wrong code looks like anyway.
        ApiResult<byte[]> header = await content.ReadAsync(links.Value.HeaderUrl, cancellationToken: cancellationToken);

        if (!header.IsSuccess)
        {
            return ApiResult.Failure<byte[]>(header.Error);
        }

        PackageHeader? packageHeader = SittingKeys.ReadHeader(header.Value);

        if (packageHeader is null || !SittingKeys.IsSupported(packageHeader))
        {
            return ApiResult.Failure<byte[]>(ReviewErrors.UnsupportedPackage);
        }

        byte[] contentKey = [];

        try
        {
            // Argon2id at the exam's own parameters: about half a second, once per sitting.
            return await Task.Run(
                () => SittingKeys.TryDeriveContentKey(packageHeader, code, out contentKey)
                    ? ApiResult.Success(SittingKeys.DeriveHandInKey(contentKey))
                    : ApiResult.Failure<byte[]>(ReviewErrors.WrongCode),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    public async Task<ApiResult<OpenedSubmission>> OpenAsync(
        Guid sittingId,
        SubmissionSummary submission,
        byte[] handInKey,
        CancellationToken cancellationToken = default)
    {
        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        if (!token.IsSuccess)
        {
            return ApiResult.Failure<OpenedSubmission>(token.Error);
        }

        ApiResult<SubmissionDownload> links = await apiClient.GetSubmissionDownloadAsync(
            submission.Id, token.Value, cancellationToken);

        if (!links.IsSuccess)
        {
            return ApiResult.Failure<OpenedSubmission>(links.Error);
        }

        // Both are checked against the digests the server took when the work arrived.
        ApiResult<byte[]> sealedSolution = await content.ReadAsync(
            links.Value.SolutionUrl, links.Value.SolutionSizeBytes, links.Value.SolutionSha256, cancellationToken);

        if (!sealedSolution.IsSuccess)
        {
            return ApiResult.Failure<OpenedSubmission>(sealedSolution.Error);
        }

        ApiResult<byte[]> sealedLog = await content.ReadAsync(
            links.Value.ActivityLogUrl, links.Value.ActivityLogSizeBytes, links.Value.ActivityLogSha256, cancellationToken);

        if (!sealedLog.IsSuccess)
        {
            return ApiResult.Failure<OpenedSubmission>(sealedLog.Error);
        }

        byte[] archive = [];

        try
        {
            if (!TryOpenSolution(sealedSolution.Value, handInKey, sittingId, out archive))
            {
                return ApiResult.Failure<OpenedSubmission>(ReviewErrors.CannotOpen);
            }

            IReadOnlyList<ExamTaskFile>? files = SittingKeys.ReadArchive(archive);

            if (files is null)
            {
                return ApiResult.Failure<OpenedSubmission>(ReviewErrors.CannotOpen);
            }

            ActivityLogContents log = ActivityLogReader.Read(ReadLines(sealedLog.Value), sittingId, handInKey);

            return ApiResult.Success(new OpenedSubmission(files, log));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(archive);
        }
    }

    // SEIS1 | nonce (12) | tag (16) | ciphertext, with the sitting's id authenticated, so a solution
    // from another sitting cannot be passed off as one of these.
    private static bool TryOpenSolution(byte[] sealedSolution, byte[] handInKey, Guid sittingId, out byte[] archive)
    {
        archive = [];

        if (sealedSolution.Length < HeaderSize || !sealedSolution.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return false;
        }

        byte[] opened = new byte[sealedSolution.Length - HeaderSize];

        try
        {
            using var aes = new AesGcm(handInKey, TagSize);
            aes.Decrypt(
                sealedSolution.AsSpan(Magic.Length, NonceSize),
                sealedSolution.AsSpan(HeaderSize),
                sealedSolution.AsSpan(Magic.Length + NonceSize, TagSize),
                opened,
                Encoding.UTF8.GetBytes($"SecureExamIDE submission v1|{sittingId:N}"));

            archive = opened;

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ReadLines(byte[] log)
    {
        using var reader = new StringReader(Encoding.ASCII.GetString(log));

        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static ReadOnlySpan<byte> Magic => "SEIS1"u8;

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 5 + NonceSize + TagSize;
    private const int PageSize = 50;
}
