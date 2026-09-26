namespace SecureExamIDE.Client.Formatting;

// "1 task file" but "3 task files", for the one-line summaries the lists show.
public static class Plural
{
    public static string Format(int count, string singular, string? plural = null) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";
}
