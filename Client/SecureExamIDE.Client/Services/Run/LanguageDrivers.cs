namespace SecureExamIDE.Client.Services.Run;

internal sealed class LanguageDrivers(IEnumerable<ILanguageDriver> drivers) : ILanguageDrivers
{
    private readonly ILanguageDriver[] _drivers = [.. drivers];

    public ILanguageDriver? ForSources(IReadOnlyList<SourceFile> sources, string? entryName = null)
    {
        SourceFile? entry = entryName is null
            ? null
            : sources.FirstOrDefault(source => string.Equals(source.Name, entryName, StringComparison.OrdinalIgnoreCase));

        return (entry is null ? null : Array.Find(_drivers, driver => driver.Claims([entry])))
            ?? Array.Find(_drivers, driver => driver.Claims(sources));
    }

    public string FileKinds => string.Join(", ", _drivers.Select(driver => driver.FileKinds));
}
