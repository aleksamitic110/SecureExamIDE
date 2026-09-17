using System.Text.Json;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Storage;

namespace SecureExamIDE.Client.Services.Session;

internal sealed class ProfileCache(string dataDirectory) : IProfileCache
{
    private string FilePath => Path.Combine(dataDirectory, "profile.json");

    public async Task<UserProfile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            await using FileStream file = File.OpenRead(FilePath);

            return await JsonSerializer.DeserializeAsync<UserProfile>(file, ApiClient.JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(dataDirectory);

        await AtomicFile.WriteAsync(
            FilePath,
            JsonSerializer.SerializeToUtf8Bytes(profile, ApiClient.JsonOptions),
            cancellationToken);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        File.Delete(FilePath);
        return Task.CompletedTask;
    }
}
