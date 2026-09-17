using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit.TextMate;
using SecureExamIDE.Client.ViewModels.ExamDay;
using TextMateSharp.Grammars;

namespace SecureExamIDE.Client.Views.ExamDay;

// Syntax highlighting is a property of the editor control, not of the view model, so choosing the
// grammar for the open file's extension happens here.
public partial class WorkspaceView : UserControl
{
    private RegistryOptions? _grammars;
    private TextMate.Installation? _textMate;
    private WorkspaceViewModel? _viewModel;

    public WorkspaceView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ThemeName theme = ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus;
        _grammars = new RegistryOptions(theme);
        _textMate = Editor.InstallTextMate(_grammars);

        ApplyGrammar();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _textMate?.Dispose();
        _textMate = null;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as WorkspaceViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyGrammar();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.ActiveFile))
        {
            ApplyGrammar();
        }
    }

    private void ApplyGrammar()
    {
        if (_textMate is null || _grammars is null || _viewModel?.ActiveFile is not { } file)
        {
            return;
        }

        string? scope = _grammars.GetScopeByExtension(Path.GetExtension(file.Name));

        // A file with no known extension is shown as plain text.
        _textMate.SetGrammar(scope);
    }
}
