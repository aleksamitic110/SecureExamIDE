using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Workspace;

internal static class WorkspaceErrors
{
    public static readonly ApiError InvalidName = new(
        0,
        "Workspace.InvalidName",
        $"Use up to {WorkspaceFileName.MaxLength} letters, digits, '.', '-' and '_', for example main.c. "
            + "A name cannot start or end with '.'.",
        []);

    public static readonly ApiError ReservedName = new(
        0,
        "Workspace.ReservedName",
        "This name is reserved by Windows and cannot be used for a file.",
        []);

    public static readonly ApiError NameTaken = new(
        0,
        "Workspace.NameTaken",
        "A file with this name already exists.",
        []);

    public static ApiError SaveFailed(string detail) => new(
        0,
        "Workspace.SaveFailed",
        $"Your work could not be saved: {detail}",
        []);
}
