using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit.TextMate;
using SecureExamIDE.Client.ViewModels.Professor;
using TextMateSharp.Grammars;

namespace SecureExamIDE.Client.Views.Professor;

// Highlighting belongs to the editor control rather than the view model, so choosing the grammar for
// the open file happens here - the same arrangement as the student's workspace.
public partial class SubmissionReviewView : UserControl
{
    private RegistryOptions? _grammars;
    private TextMate.Installation? _textMate;
    private SubmissionReviewViewModel? _viewModel;

    public SubmissionReviewView()
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

        _viewModel = DataContext as SubmissionReviewViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyGrammar();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubmissionReviewViewModel.SelectedFile))
        {
            ApplyGrammar();
        }
    }

    private void ApplyGrammar()
    {
        if (_textMate is null || _grammars is null || _viewModel?.SelectedFile is not { } file)
        {
            return;
        }

        _textMate.SetGrammar(_grammars.GetScopeByExtension(Path.GetExtension(file.Name)));
    }
}
