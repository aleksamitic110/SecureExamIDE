using System.Security.Cryptography;
using System.Text;
using AvaloniaEdit.Document;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Unlock;

namespace SecureExamIDE.Client.ViewModels.Professor;

// One file out of a student's sealed solution, held in memory and wiped when the screen is left. The
// TextDocument is what the read-only editor shows, so the professor reads the code with the same
// highlighting the student wrote it with.
public sealed class ReviewedFileItem
{
    private readonly ExamTaskFile _file;

    public ReviewedFileItem(ExamTaskFile file)
    {
        _file = file;

        // Source code is text; a binary a student added is named but not shown.
        string? text = IsText(file) ? Encoding.UTF8.GetString(file.Content).TrimStart('﻿') : null;

        IsShown = text is not null;
        Document = new TextDocument(text ?? "This file is not text and cannot be shown here.");
    }

    public string Name => _file.Name;

    public string Size => ByteSize.Format(_file.Content.LongLength);

    public bool IsShown { get; }

    public TextDocument Document { get; }

    public void Wipe() => CryptographicOperations.ZeroMemory(_file.Content);

    private static bool IsText(ExamTaskFile file) => Array.IndexOf(file.Content, (byte)0) < 0;
}
