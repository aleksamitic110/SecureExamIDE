using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Workspace;

// The rules for a file the student creates. The workspace is one flat folder, and the name becomes a
// real file name on disk and, later, an argument on a compiler's command line - so it is kept to a
// character set that means the same thing on Windows and Linux and needs no quoting: no separators
// (nothing can point outside the folder), no spaces, and no names Windows refuses to create.
internal static class WorkspaceFileName
{
    public const int MaxLength = 100;

    public static ApiResult<string> Validate(string? name, IEnumerable<string> existingNames, string? renaming = null)
    {
        string trimmed = name?.Trim() ?? string.Empty;

        if (!IsSafe(trimmed))
        {
            return ApiResult.Failure<string>(
                IsReserved(trimmed) ? WorkspaceErrors.ReservedName : WorkspaceErrors.InvalidName);
        }

        // Windows file names ignore case, so "Main.c" and "main.c" are one file there.
        bool taken = existingNames.Any(existing =>
            string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(existing, renaming, StringComparison.Ordinal));

        return taken
            ? ApiResult.Failure<string>(WorkspaceErrors.NameTaken)
            : ApiResult.Success(trimmed);
    }

    // Also the store's own guard, so a name that skipped validation still cannot leave the folder.
    public static bool IsSafe(string name) =>
        name.Length is > 0 and <= MaxLength &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_') &&
        !name.StartsWith('.') &&
        !name.EndsWith('.') &&
        // Saving writes "<name>.tmp" first; a student file with that name would be overwritten by it.
        !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
        !IsReserved(name);

    private static bool IsReserved(string name)
    {
        int dot = name.IndexOf('.', StringComparison.Ordinal);
        string stem = (dot < 0 ? name : name[..dot]).ToUpperInvariant();

        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
               (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                stem[3] is >= '1' and <= '9');
    }
}
