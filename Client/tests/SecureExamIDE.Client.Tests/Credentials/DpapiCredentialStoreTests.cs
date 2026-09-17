using System.Runtime.Versioning;
using System.Text;
using SecureExamIDE.Client.Services.Credentials;

namespace SecureExamIDE.Client.Tests.Credentials;

// These run on the Windows machine only; on Linux they are reported as skipped.
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly StoredCredential Credential =
        new("ana@example.com", Guid.NewGuid(), "a-device-secret-that-must-never-appear-on-disk");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [WindowsOnlyFact]
    public async Task Load_Should_ReturnWhatWasSaved()
    {
        // Arrange
        var store = new DpapiCredentialStore(_directory);
        await store.SaveAsync(Credential);

        // Act
        StoredCredential? loaded = await store.LoadAsync();

        // Assert
        loaded.ShouldBe(Credential);
    }

    [WindowsOnlyFact]
    public async Task Save_Should_NeverWriteTheSecretInTheClear()
    {
        // Arrange
        var store = new DpapiCredentialStore(_directory);

        // Act
        await store.SaveAsync(Credential);

        // Assert
        string contents = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Directory.GetFiles(_directory).Single()));
        contents.ShouldNotContain(Credential.DeviceCredential);
    }

    [WindowsOnlyFact]
    public async Task Load_Should_ReturnNothing_WhenTheFileWasEdited()
    {
        // Arrange
        var store = new DpapiCredentialStore(_directory);
        await store.SaveAsync(Credential);

        string path = Directory.GetFiles(_directory).Single();
        byte[] bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);

        // Act
        StoredCredential? loaded = await store.LoadAsync();

        // Assert
        loaded.ShouldBeNull();
    }
}
