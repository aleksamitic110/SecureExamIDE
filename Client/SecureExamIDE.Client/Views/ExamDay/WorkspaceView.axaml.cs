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
    // The widths the student last dragged the panels to, so putting a panel away and bringing it back
    // does not lose its size. A hidden panel's column is collapsed to nothing, which is what gives the
    // room to the editor - an invisible panel alone would leave its column sitting there empty.
    private GridLength _filesWidth = new(220);
    private GridLength _tasksWidth = new(360);
    private GridLength _consoleHeight = new(160);

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
        ApplyPanels();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceViewModel.ActiveFile):
                ApplyGrammar();
                break;

            case nameof(WorkspaceViewModel.IsFilesPanelShown):
            case nameof(WorkspaceViewModel.IsTasksPanelShown):
            case nameof(WorkspaceViewModel.IsConsolePanelShown):
                ApplyPanels();
                break;

            default:
                break;
        }
    }

    private void ApplyPanels()
    {
        if (_viewModel is null)
        {
            return;
        }

        Collapse(PanelGrid.ColumnDefinitions[FilesColumn], PanelGrid.ColumnDefinitions[FilesSplitterColumn], _viewModel.IsFilesPanelShown, ref _filesWidth);
        Collapse(PanelGrid.ColumnDefinitions[TasksColumn], PanelGrid.ColumnDefinitions[TasksSplitterColumn], _viewModel.IsTasksPanelShown, ref _tasksWidth);
        Collapse(EditorGrid.RowDefinitions[ConsoleRow], EditorGrid.RowDefinitions[ConsoleSplitterRow], _viewModel.IsConsolePanelShown, ref _consoleHeight);
    }

    private static void Collapse(ColumnDefinition panel, ColumnDefinition splitter, bool shown, ref GridLength width)
    {
        if (shown)
        {
            panel.Width = width;
            splitter.Width = SplitterThickness;

            return;
        }

        if (panel.Width.Value > 0)
        {
            width = panel.Width;
        }

        panel.Width = Collapsed;
        splitter.Width = Collapsed;
    }

    private static void Collapse(RowDefinition panel, RowDefinition splitter, bool shown, ref GridLength height)
    {
        if (shown)
        {
            panel.Height = height;
            splitter.Height = SplitterThickness;

            return;
        }

        if (panel.Height.Value > 0)
        {
            height = panel.Height;
        }

        panel.Height = Collapsed;
        splitter.Height = Collapsed;
    }

    private const int FilesColumn = 0;
    private const int FilesSplitterColumn = 1;
    private const int TasksSplitterColumn = 3;
    private const int TasksColumn = 4;
    private const int ConsoleSplitterRow = 2;
    private const int ConsoleRow = 3;

    private static readonly GridLength Collapsed = new(0);

    private static readonly GridLength SplitterThickness = new(4);

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
