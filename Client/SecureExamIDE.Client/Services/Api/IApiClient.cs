namespace SecureExamIDE.Client.Services.Api;

public interface IApiClient
{
    Task<ApiResult<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ResendVerificationCodeAsync(
        ResendVerificationCodeRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<DeviceTokenResponse>> IssueDeviceTokenAsync(
        DeviceTokenRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<UserProfile>> GetUserAsync(Guid userId, string accessToken, CancellationToken cancellationToken = default);

    Task<ApiResult> RevokeDeviceAsync(Guid deviceId, string accessToken, CancellationToken cancellationToken = default);

    Task<ApiResult<PagedList<CatalogExam>>> GetPublishedExamsAsync(
        int page,
        int pageSize,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult<PagedList<ExamSitting>>> GetExamSittingsAsync(
        Guid examId,
        int page,
        int pageSize,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult<SittingPackage>> GetSittingPackageAsync(
        Guid sittingId,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult<PagedList<ExamDependency>>> GetExamDependenciesAsync(
        Guid examId,
        int page,
        int pageSize,
        DependencyPlatform? platform,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult<UploadedSubmissionContent>> UploadSubmissionContentAsync(
        Guid sittingId,
        byte[] solution,
        byte[] activityLog,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult<SubmissionReceipt>> CreateSubmissionAsync(
        Guid sittingId,
        string solutionObjectKey,
        string activityLogObjectKey,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult<DependencyDownload>> GetDependencyDownloadAsync(
        Guid dependencyId,
        string accessToken,
        CancellationToken cancellationToken = default);

    // The professor's own exams, drafts included, newest first.
    Task<ApiResult<PagedList<MyExam>>> GetMyExamsAsync(
        int page,
        int pageSize,
        ExamStatus? status,
        string accessToken,
        CancellationToken cancellationToken = default);

    // One exam in full. Scoped to its owner, so another professor's exam is reported as missing.
    Task<ApiResult<ExamDetails>> GetExamAsync(Guid examId, string accessToken, CancellationToken cancellationToken = default);

    Task<ApiResult<Guid>> CreateExamAsync(
        string title,
        string description,
        string subject,
        string accessToken,
        CancellationToken cancellationToken = default);

    // The API changes only the fields it is sent; the editor holds all three, so it sends all three.
    Task<ApiResult> UpdateExamAsync(
        Guid examId,
        string title,
        string description,
        string subject,
        string accessToken,
        CancellationToken cancellationToken = default);

    // Throws a draft away, its stored files and dependencies with it. Refused once published.
    Task<ApiResult> DeleteExamAsync(Guid examId, string accessToken, CancellationToken cancellationToken = default);

    // Phase one for a task file: the bytes go through the API, so the digest it records is its own
    // measurement. Nothing is written to the database yet.
    Task<ApiResult<UploadedExamFile>> UploadExamFileContentAsync(
        Guid examId,
        string fileName,
        Stream content,
        string contentType,
        string accessToken,
        CancellationToken cancellationToken = default);

    // Phase two: the uploaded object becomes a task file of the exam.
    Task<ApiResult<Guid>> AddExamFileAsync(
        Guid examId,
        string objectKey,
        string fileName,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveExamFileAsync(
        Guid examId,
        Guid fileId,
        string accessToken,
        CancellationToken cancellationToken = default);

    // Phase one for a toolchain: a presigned link to PUT the archive straight into storage.
    Task<ApiResult<DependencyUploadTarget>> CreateDependencyUploadUrlAsync(
        Guid examId,
        string accessToken,
        CancellationToken cancellationToken = default);

    // Phase two: the uploaded archive becomes a dependency. The platform is required - defaulting it
    // would offer a Windows-only compiler to Linux students, who would find out in the exam room.
    Task<ApiResult<Guid>> AddExamDependencyAsync(
        Guid examId,
        string objectKey,
        string name,
        string version,
        DependencyPlatform platform,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveExamDependencyAsync(
        Guid examId,
        Guid dependencyId,
        string accessToken,
        CancellationToken cancellationToken = default);

    // Puts the exam in the student catalog. There is no going back, and no further change to it.
    Task<ApiResult> PublishExamAsync(Guid examId, string accessToken, CancellationToken cancellationToken = default);

    // Schedules a sitting: the API seals the exam's files into a package for this sitting alone and
    // answers with the one-time code that opens it. Only for a published exam, and the code is in
    // the reply once and never again.
    Task<ApiResult<ScheduledSitting>> CreateExamSessionAsync(
        Guid examId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        string accessToken,
        CancellationToken cancellationToken = default);

    // The professor's own sittings, cancelled ones included, optionally for one exam.
    Task<ApiResult<PagedList<MySitting>>> GetMySittingsAsync(
        int page,
        int pageSize,
        Guid? examId,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResult> CancelSittingAsync(Guid sittingId, string accessToken, CancellationToken cancellationToken = default);
}
