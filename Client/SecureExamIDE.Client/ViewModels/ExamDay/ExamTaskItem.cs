using System.Text;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Unlock;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

public sealed class ExamTaskItem(ExamTaskFile file)
{
    public string Name => file.Name;

    public string Size => ByteSize.Format(file.Content.LongLength);

    // Plain text and Markdown are shown as they are. Anything else - a PDF above all - waits for the
    // workspace, which will display it without writing the decrypted file to disk.
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
