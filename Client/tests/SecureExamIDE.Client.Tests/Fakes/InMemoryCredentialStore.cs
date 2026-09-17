using SecureExamIDE.Client.Services.Credentials;

namespace SecureExamIDE.Client.Tests.Fakes;

// Stands in for the OS secret store in tests of the code that uses one.
internal sealed class InMemoryCredentialStore : ICredentialStore
{
    public StoredCredential? Stored { get; set; }

    public Task<StoredCredential?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);

    public Task SaveAsync(StoredCredential credential, CancellationToken cancellationToken = default)
    {
        Stored = credential;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        Stored = null;
        return Task.CompletedTask;
    }
}
