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
}
