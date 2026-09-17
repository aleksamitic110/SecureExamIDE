using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Unlock;

namespace SecureExamIDE.Client.Tests.Unlock;

public sealed class PackageUnlockerTests : IDisposable
{
    private static readonly Dictionary<string, string> Tasks = new()
    {
        ["task1.md"] = "# Task 1\nFind the shortest paths.",
        ["task2.txt"] = "Task 2: knapsack"
    };

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PackageUnlocker _unlocker = new();

    private string PackagePath => Path.Combine(_directory, "package.bin");

    private string HeaderPath => Path.Combine(_directory, "package.hdr");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<string> WriteSealedAsync(Func<PackageHeader, PackageHeader>? alterHeader = null)
    {
        (byte[] package, byte[] header, string sha256) = TestSealer.Seal(Tasks, alterHeader: alterHeader);

        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(PackagePath, package);
        await File.WriteAllBytesAsync(HeaderPath, header);

        return sha256;
    }

    [Fact]
    public async Task Unlock_Should_RecoverEveryTaskFile_WithTheRightCode()
    {
        // Arrange
        string sha256 = await WriteSealedAsync();

        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, sha256, TestSealer.Code);

        // Assert
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        using UnlockedExam exam = result.Value;
        exam.Files.Select(f => f.Name).ShouldBe(["task1.md", "task2.txt"]);
        System.Text.Encoding.UTF8.GetString(exam.Files[1].Content).ShouldBe("Task 2: knapsack");
    }

    // Typed as someone copying it off a whiteboard would: lower case, no dashes, O for 0, l for 1.
    [Fact]
    public async Task Unlock_Should_AcceptTheCodeTypedLoosely()
    {
        // Arrange
        string sha256 = await WriteSealedAsync();

        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, sha256, "b34k xO88 dl2w 75y6 mjqx");

        // Assert
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Dispose();
    }

    [Fact]
    public async Task Unlock_Should_RefuseAWrongCode()
    {
        // Arrange
        string sha256 = await WriteSealedAsync();

        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, sha256, "B34K-X088-D12W-75Y6-MJQY");

        // Assert
        result.Error!.Code.ShouldBe("Unlock.WrongCode");
    }

    [Fact]
    public async Task Unlock_Should_NotEvenTry_AnIncompleteCode()
    {
        // Arrange
        string sha256 = await WriteSealedAsync();

        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, sha256, "B34K-X088");

        // Assert
        result.Error!.Code.ShouldBe("Unlock.IncompleteCode");
    }

    // A package altered on disk is caught before the slow key derivation and reported as damaged,
    // so the student does not keep retyping a correct code.
    [Fact]
    public async Task Unlock_Should_ReportAnAlteredPackageAsDamaged()
    {
        // Arrange
        string sha256 = await WriteSealedAsync();
        byte[] bytes = await File.ReadAllBytesAsync(PackagePath);
        bytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(PackagePath, bytes);

        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, sha256, TestSealer.Code);

        // Assert
        result.Error!.Code.ShouldBe("Unlock.Damaged");
    }

    // Even with the digest check out of the way, AES-GCM itself refuses altered ciphertext.
    [Fact]
    public async Task Unlock_Should_RefuseAlteredCiphertext_EvenWhenTheDigestWasRecomputed()
    {
        // Arrange
        await WriteSealedAsync();
        byte[] bytes = await File.ReadAllBytesAsync(PackagePath);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(PackagePath, bytes);
        string recomputed = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, recomputed, TestSealer.Code);

        // Assert
        result.Error!.Code.ShouldBe("Unlock.Damaged");
    }

    [Fact]
    public async Task Unlock_Should_RefuseAHeaderDemandingUnreasonableMemory()
    {
        // Arrange
        string sha256 = await WriteSealedAsync(header => header with { KdfMemoryKib = 64 * 1024 * 1024 });

        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, sha256, TestSealer.Code);

        // Assert
        result.Error!.Code.ShouldBe("Unlock.Unsupported");
    }

    // If the server ever folds codes differently, the header says so and the client refuses rather
    // than deriving a key that can never match.
    [Fact]
    public async Task Unlock_Should_RefuseAnUnknownNormalizationRule()
    {
        // Arrange
        string sha256 = await WriteSealedAsync(header => header with { CodeNormalization = "lowercase" });

        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, sha256, TestSealer.Code);

        // Assert
        result.Error!.Code.ShouldBe("Unlock.Unsupported");
    }

    [Fact]
    public async Task Unlock_Should_ExplainAMissingPackage()
    {
        // Act
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, new string('a', 64), TestSealer.Code);

        // Assert
        result.Error!.Code.ShouldBe("Unlock.Missing");
    }

    [Fact]
    public async Task Dispose_Should_WipeTheDecryptedTasks()
    {
        // Arrange
        string sha256 = await WriteSealedAsync();
        ApiResult<UnlockedExam> result = await _unlocker.UnlockAsync(PackagePath, HeaderPath, sha256, TestSealer.Code);
        byte[] content = result.Value.Files[0].Content;

        // Act
        result.Value.Dispose();

        // Assert
        content.ShouldAllBe(b => b == 0);
    }
}
