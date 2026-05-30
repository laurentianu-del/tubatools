using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Services;

public interface IBuiltinTool
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Glyph { get; }
    string Category { get; }
    BuiltinToolKind Kind { get; }

    Task ExecuteAsync(BuiltinToolContext context);
}

public enum BuiltinToolKind
{
    Dialog,
    BackgroundTask,
    ProgressTask,
    InstantAction
}

public sealed class BuiltinToolContext
{
    public XamlRoot XamlRoot { get; init; } = null!;
    public Action<string>? OnProgress { get; init; }
    public Func<string, string, ContentDialogResult>? ShowDialog { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Creates a ContentDialog that automatically inherits the current app theme.
    /// </summary>
    public ContentDialog CreateDialog(string title, string closeButtonText = "关闭")
    {
        return new ContentDialog
        {
            Title = title,
            CloseButtonText = closeButtonText,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
    }
}
