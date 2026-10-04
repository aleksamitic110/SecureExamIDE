using System.Security.Cryptography;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Credentials;

namespace SecureExamIDE.Client.Services.Unlock;

// The student's side of the sealing scheme, run on exam day with no connection. The derivation itself
// lives in SittingKeys, which the professor's review shares; what is here is the order of the checks
// and what each failure is called.
internal sealed class PackageUnlocker(IMachineIdentity machineIdentity) : IPackageUnlocker
{
    public Task<ApiResult<UnlockedExam>> UnlockAsync(
        string packagePath,
        string headerPath,
        string expectedPackageSha256,
        string typedCode,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Unlock(packagePath, headerPath, expectedPackageSha256, typedCode), cancellationToken);

    private ApiResult<UnlockedExam> Unlock(
        string packagePath,
        string headerPath,
        string expectedPackageSha256,
        string typedCode)
    {
        string code = OneTimeCode.Normalize(typedCode);

        if (code.Length != OneTimeCode.Length)
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.IncompleteCode);
        }

        if (!File.Exists(packagePath) || !File.Exists(headerPath))
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.Missing);
        }

        PackageHeader? header = SittingKeys.ReadHeader(File.ReadAllBytes(headerPath));

        if (header is null)
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.Damaged);
        }

        if (!SittingKeys.IsSupported(header))
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.Unsupported);
        }

        byte[] ciphertext = File.ReadAllBytes(packagePath);

        // Checked before the slow key derivation: a package that is not the one downloaded is
        // reported as damaged, not as a wrong code the student would keep retyping.
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(ciphertext)), expectedPackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResult.Failure<UnlockedExam>(UnlockErrors.Damaged);
        }

        byte[] contentKey = [];
        byte[] archive = [];

        try
        {
            if (!SittingKeys.TryDeriveContentKey(header, code, out contentKey))
            {
                return ApiResult.Failure<UnlockedExam>(UnlockErrors.WrongCode);
            }

            if (!SittingKeys.TryOpen(contentKey, null, header.PackageNonce, header.PackageTag, ciphertext, out archive))
            {
                return ApiResult.Failure<UnlockedExam>(UnlockErrors.Damaged);
            }

            IReadOnlyList<ExamTaskFile>? files = SittingKeys.ReadArchive(archive);

#pragma warning disable CA2000 // Ownership passes to the caller, which disposes it when the exam is locked again.
            return files is null
                ? ApiResult.Failure<UnlockedExam>(UnlockErrors.Damaged)
                : ApiResult.Success(new UnlockedExam(
                    files,
                    SittingKeys.DeriveWorkspaceKey(contentKey, machineIdentity.GetMachineId()),
                    SittingKeys.DeriveHandInKey(contentKey)));
#pragma warning restore CA2000
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(archive);
        }
    }
}
