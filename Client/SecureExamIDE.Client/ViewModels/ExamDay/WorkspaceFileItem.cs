using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecureExamIDE.Client.ViewModels.ExamDay;

// One of the student's files. The TextDocument is the editor's model - text, caret-independent undo
// history - so switching tabs keeps each file's undo stack.
public sealed partial class WorkspaceFileItem(string name, TextDocument document) : ObservableObject
{
    [ObservableProperty]
    private string _name = name;

    // Typed since the last save; the tab shows a dot until the autosave catches up.
    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isActive;

    public TextDocument Document { get; } = document;
}
