using System.Text;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Unlock;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

public sealed class ExamTaskItem(ExamTaskFile file)
{
    public string Name => file.Name;

    public bool IsPdf => Path.GetExtension(file.Name).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    // The decrypted bytes, handed to the PDF renderer. They stay in memory and are wiped with the
    // rest of the unlocked exam.
    public byte[] Content => file.Content;

    public string Size => ByteSize.Format(file.Content.LongLength);

    // Plain text and Markdown are shown as they are; a PDF is drawn by the PDF renderer, also from
    // memory. Anything else is named but not shown.
    public string? Text => IsText(file) ? DecodeUtf8(file.Content) : null;

    private static bool IsText(ExamTaskFile task)
    {
        string extension = Path.GetExtension(task.Name).ToUpperInvariant();

        return extension is ".TXT" or ".MD" or ".MARKDOWN" or ".C" or ".CPP" or ".H" or ".HPP" or ".CS" or ".JAVA" or ".PY" or ".JSON" or ".XML" or ".CSV" ||
               (extension.Length == 0 && Array.IndexOf(task.Content, (byte)0) < 0);
    }

    private static string DecodeUtf8(byte[] content) =>
        Encoding.UTF8.GetString(content).TrimStart('﻿');
}
