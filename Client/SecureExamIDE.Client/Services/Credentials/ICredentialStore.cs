namespace SecureExamIDE.Client.Services.Credentials;

// Keeps the device credential in the operating system's secret store. Load returns null when there
// is nothing usable - no credential yet, or one that was tampered with or copied from another
// machine - so the caller treats all of those as "this computer is not signed in".
public interface ICredentialStore
{
    Task<StoredCredential?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(StoredCredential credential, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
