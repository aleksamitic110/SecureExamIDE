namespace SecureExamIDE.Client.Services.Run;

internal sealed class LanguageDrivers(IEnumerable<ILanguageDriver> drivers) : ILanguageDrivers
{
    private readonly ILanguageDriver[] _drivers = [.. drivers];

    public ILanguageDriver? ForSources(IReadOnlyList<SourceFile> sources) =>
        Array.Find(_drivers, driver => driver.Claims(sources));

    public string FileKinds => string.Join(", ", _drivers.Select(driver => driver.FileKinds));
}
