using System.Text.Json;

namespace SecureExamIDE.Client.Services.Storage;

internal sealed class UiPreferences(string directory) : IUiPreferences
{
    public WorkspacePanels ReadPanels()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<WorkspacePanels>(File.ReadAllBytes(Path), JsonOptions) ?? new WorkspacePanels()
                : new WorkspacePanels();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A preference is never worth an error on screen: the layout simply starts as it does.
            return new WorkspacePanels();
        }
    }

    public void WritePanels(WorkspacePanels panels)
    {
        try
        {
            Directory.CreateDirectory(directory);
            AtomicFile.Write(Path, JsonSerializer.SerializeToUtf8Bytes(panels, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // As above: not being able to remember the layout changes nothing about the exam.
        }
    }

    private string Path => System.IO.Path.Combine(directory, "workspace-layout.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
