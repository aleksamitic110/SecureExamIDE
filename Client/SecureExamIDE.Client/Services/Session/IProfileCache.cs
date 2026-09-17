using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Session;

// The last profile the server returned for this computer's account. Only used offline, on exam day,
// to show whose computer it is; it grants nothing - the credential is what signs in.
public interface IProfileCache
{
    Task<UserProfile?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(UserProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
