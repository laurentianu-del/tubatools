using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 内置工具独立窗口宿主：把工具页面放到一个新 Window 的 Frame 中。
/// 由 MainWindow.NavigateToToolPage 在设置"独立窗口"模式下调用，
/// 关闭窗口时触发 ToolContentPageParam.OnClose 清理（与嵌入模式一致）。
/// </summary>
public sealed class BuiltinToolWindow
{
    private static readonly List<BuiltinToolWindow> _openWindows = [];

    /// <summary>最近激活的工具窗口；页面内"返回/关闭"按钮通过它路由到正确窗口。</summary>
    public static BuiltinToolWindow? ActiveWindow { get; private set; }

    private readonly Window _window;
    private readonly Frame _frame;

    private BuiltinToolWindow(Type pageType, object? parameter, string title)
    {
        _window = new Window();
        _frame = new Frame { CacheSize = 10 };
        // 与其它独立窗口一致：跟随应用主题设置（跟随系统时为 Default）
        _frame.RequestedTheme = ThemeService.CurrentElementTheme;
        _frame.Navigate(pageType, parameter);
        _window.Content = _frame;
        BuiltinWindowHelper.ApplyStandardStyle(_window, title);
        _window.Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                ActiveWindow = this;
        };
        _window.Closed += (_, _) => OnClosed();
        _openWindows.Add(this);
    }

    public static void Show(Type pageType, object? parameter, string title)
    {
        var instance = new BuiltinToolWindow(pageType, parameter, title);
        instance._window.Activate();
        ActiveWindow = instance;
    }

    public void Close() => _window.Close();

    /// <summary>页面内"返回/关闭"按钮调用：能后退则后退，否则关闭窗口。</summary>
    public void GoBackOrClose()
    {
        if (_frame.CanGoBack)
            _frame.GoBack();
        else
            _window.Close();
    }

    private void OnClosed()
    {
        // 窗口关闭时 Frame 不会触发 OnNavigatedFrom，需手动执行与嵌入模式一致的清理
        if (_frame.Content is ToolContentPage page)
            page.Detach();
        if (ActiveWindow == this)
            ActiveWindow = _openWindows.LastOrDefault(w => w != this);
        _openWindows.Remove(this);
    }
}
