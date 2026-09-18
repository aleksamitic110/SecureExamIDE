using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Exams;

namespace SecureExamIDE.Client.Services.Toolchains;

// Turns the archives downloaded with an exam into compilers that can be run. Unpacking happens once,
// right after the download, so exam day does not start with a wait; asking again for an exam that is
// already unpacked only looks at the folders.
public interface IToolchainService
{
    Task<ApiResult<IReadOnlyList<Toolchain>>> PrepareAsync(
        DownloadedExam exam,
        CancellationToken cancellationToken = default);
}
