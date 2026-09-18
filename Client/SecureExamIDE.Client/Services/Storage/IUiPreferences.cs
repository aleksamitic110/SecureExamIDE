namespace SecureExamIDE.Client.Services.Storage;

// The few choices about the workspace's own look that should survive closing the application: which
// panels the student keeps open. Nothing here is secret, and nothing here belongs to an exam - it is
// how this person likes to work.
public interface IUiPreferences
{
    WorkspacePanels ReadPanels();

    void WritePanels(WorkspacePanels panels);
}

public sealed record WorkspacePanels(bool Files = true, bool Tasks = true, bool Console = true);
