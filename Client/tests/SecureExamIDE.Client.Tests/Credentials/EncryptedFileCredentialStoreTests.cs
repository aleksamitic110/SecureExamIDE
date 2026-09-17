using System.Text;
using SecureExamIDE.Client.Services.Credentials;

namespace SecureExamIDE.Client.Tests.Credentials;

public sealed class EncryptedFileCredentialStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly StoredCredential Credential =
        new("ana@example.com", Guid.NewGuid(), "a-device-secret-that-must-never-appear-on-disk");

    private EncryptedFileCredentialStore StoreOn(string machineId) => new(_directory, new FixedMachine(machineId));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Load_Should_ReturnWhatWasSaved()
    {
        // Arrange
        EncryptedFileCredentialStore store = StoreOn("machine-a");
        await store.SaveAsync(Credential);

        // Act
        StoredCredential? loaded = await store.LoadAsync();

        // Assert
        loaded.ShouldBe(Credential);
    }

    [Fact]
    public async Task Save_Should_NeverWriteTheSecretInTheClear()
    {
        // Arrange
        EncryptedFileCredentialStore store = StoreOn("machine-a");

        // Act
        await store.SaveAsync(Credential);

        // Assert
        foreach (string file in Directory.GetFiles(_directory))
        {
            string contents = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            contents.ShouldNotContain(Credential.DeviceCredential);
        }
    }

    // The folder copied to another computer must open nothing.
    [Fact]
    public async Task Load_Should_ReturnNothing_OnAnotherMachine()
    {
        // Arrange
        await StoreOn("machine-a").SaveAsync(Credential);

        // Act
        StoredCredential? loaded = await StoreOn("machine-b").LoadAsync();

        // Assert
        loaded.ShouldBeNull();
    }

    // GCM authentication: an edited file is detected rather than decrypted into garbage.
    [Fact]
    public async Task Load_Should_ReturnNothing_WhenTheFileWasEdited()
    {
        // Arrange
        EncryptedFileCredentialStore store = StoreOn("machine-a");
        await store.SaveAsync(Credential);

        string sealedPath = Directory.GetFiles(_directory, "*.sealed").Single();
        byte[] bytes = await File.ReadAllBytesAsync(sealedPath);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(sealedPath, bytes);

        // Act
        StoredCredential? loaded = await store.LoadAsync();

        // Assert
        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_Should_ForgetTheCredential()
    {
        // Arrange
        EncryptedFileCredentialStore store = StoreOn("machine-a");
        await store.SaveAsync(Credential);

        // Act
        await store.DeleteAsync();

        // Assert
        (await store.LoadAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Load_Should_ReturnNothing_WhenNothingWasEverSaved()
    {
        // Act
        StoredCredential? loaded = await StoreOn("machine-a").LoadAsync();

        // Assert
        loaded.ShouldBeNull();
    }

    private sealed class FixedMachine(string id) : IMachineIdentity
    {
        public string GetMachineId() => id;
    }
}
