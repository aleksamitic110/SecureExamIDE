using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SecureExamIDE.Client.Views.Professor;

public partial class SittingsView : UserControl
{
    public SittingsView()
    {
        InitializeComponent();
    }

    // Copying is the window's business, not the view model's: it needs the window's clipboard, and
    // the code is already on screen. The button says so for a moment, since a copy that shows nothing
    // looks like a button that did nothing.
    private async void OnCopyCode(object? sender, RoutedEventArgs e)
    {
        if (NewCodeText.Text is not { Length: > 0 } code || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(code);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
            // The clipboard was held by another program; the code is still on screen to write down.
            return;
        }

        CopyCodeButton.Content = "Copied";

        DispatcherTimer.RunOnce(() => CopyCodeButton.Content = "Copy", TimeSpan.FromSeconds(2));
    }
}
