using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public static class WindowSizeService
{
    private const string WidthKey = "WindowWidth";
    private const string HeightKey = "WindowHeight";
    private const string MaximizedKey = "WindowMaximized";

    public static (int Width, int Height, bool Maximized) LoadWindowSize()
    {
        int width = AppSettings.GetInt(WidthKey, 1200);
        int height = AppSettings.GetInt(HeightKey, 800);
        width = Math.Max(800, width);
        height = Math.Max(600, height);

        bool maximized = AppSettings.GetBool(MaximizedKey);

        return (width, height, maximized);
    }

    public static void SaveWindowSize(MainWindow window)
    {
        if (window is null) return;

        var appWindow = window.AppWindow;
        if (appWindow is null) return;

        var presenter = appWindow.Presenter as OverlappedPresenter;
        var isMaximized = presenter?.State == OverlappedPresenterState.Maximized;

        if (isMaximized)
        {
            AppSettings.Set(MaximizedKey, true);
        }
        else
        {
            AppSettings.Set(MaximizedKey, false);
            AppSettings.Set(WidthKey, appWindow.Size.Width);
            AppSettings.Set(HeightKey, appWindow.Size.Height);
        }
    }

    public static void ApplySavedWindowSize(MainWindow window)
    {
        if (window is null) return;

        var (width, height, maximized) = LoadWindowSize();
        var appWindow = window.AppWindow;

        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        if (maximized)
        {
            var presenter = appWindow.Presenter as OverlappedPresenter;
            presenter?.Maximize();
        }
    }
}
