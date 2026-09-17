using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.Tests.Fakes;

internal sealed class InMemoryProfileCache : IProfileCache
{
    public UserProfile? Stored { get; set; }

    public Task<UserProfile?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);

    public Task SaveAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        Stored = profile;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        Stored = null;
        return Task.CompletedTask;
    }
}
