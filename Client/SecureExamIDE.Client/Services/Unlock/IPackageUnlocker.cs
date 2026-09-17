using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Unlock;

public interface IPackageUnlocker
{
    // Opens a downloaded package with the typed code, with no network. The key derivation is
    // deliberately slow - it is what makes guessing codes expensive - so this runs off the UI thread.
    Task<ApiResult<UnlockedExam>> UnlockAsync(
        string packagePath,
        string headerPath,
        string expectedPackageSha256,
        string typedCode,
        CancellationToken cancellationToken = default);
}
