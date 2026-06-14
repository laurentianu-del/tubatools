# Multiple Windows & AppWindow

WinUI 3 supports multi-window via the `Window` class and `AppWindow` API. Each window is a
separate `Window` instance with its own `AppWindow` for sizing, positioning, and chrome control.

---

## Proportional Window Sizing (Recommended)

**Always size new windows relative to the screen work area**, never hard-code pixel dimensions.
Different users have different DPI, resolution, and display scales. A fixed `900x720` window
looks reasonable on 1080p but tiny on 4K.

```csharp
// In the Window constructor, after InitializeComponent()
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
var displayArea = DisplayArea.GetFromWindowId(windowId);
var workArea = displayArea.WorkArea;

var width = (int)(workArea.Width * 0.55);   // 55% of screen width
var height = (int)(workArea.Height * 0.70);  // 70% of screen height

AppWindow.Resize(new SizeInt32(width, height));
AppWindow.Move(new PointInt32(
    (workArea.Width - width) / 2,
    (workArea.Height - height) / 2));
```

### Proportion Guidelines

| Window Type | Width % | Height % | Notes |
|---|---|---|---|
| Main / primary window | 60–70% | 75–85% | The app shell; largest window |
| Tool / detail window | 45–55% | 60–70% | Secondary windows (port viewer, editor, error report) |
| Small dialog-style window | 30–40% | 40–50% | Confirmation, simple forms |
| Full monitor / dashboard | 80–90% | 80–90% | Hardware monitor, real-time dashboards |

Always **center** the window after sizing using `AppWindow.Move` with the offset calculation.

---

## Open a Secondary Window

```csharp
// From any page or service
private void OpenToolWindow_Click(object sender, RoutedEventArgs e)
{
    var toolWindow = new PortViewerWindow();
    toolWindow.Activate();
}
```

```csharp
// PortViewerWindow.xaml.cs
using Microsoft.UI.Windowing;
using TubaWinUi3.Services;
using Windows.Graphics;

public sealed partial class PortViewerWindow : Window
{
    public PortViewerWindow()
    {
        InitializeComponent();

        AppWindow.Title = "端口占用";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        // Proportional sizing
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId);
        var workArea = displayArea.WorkArea;

        var width = (int)(workArea.Width * 0.50);
        var height = (int)(workArea.Height * 0.65);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (workArea.Width - width) / 2,
            (workArea.Height - height) / 2));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;
    }
}
```

---

## Custom Title Bar

```xaml
<!-- MainWindow.xaml -->
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="48" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <TitleBar
        x:Name="AppTitleBar"
        Title="My App"
        IsBackButtonVisible="{x:Bind NavFrame.CanGoBack, Mode=OneWay}"
        IsPaneToggleButtonVisible="True"
        BackRequested="TitleBar_BackRequested"
        PaneToggleRequested="TitleBar_PaneToggleRequested">
        <TitleBar.IconSource>
            <ImageIconSource ImageSource="ms-appx:///Assets/AppIcon.ico" />
        </TitleBar.IconSource>
    </TitleBar>

    <!-- Row 1: content -->
</Grid>
```

```csharp
// MainWindow.xaml.cs
public MainWindow()
{
    InitializeComponent();

    ExtendsContentIntoTitleBar = true;
    SetTitleBar(AppTitleBar);
    AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
}
```

---

## Window Sizing Constraints (Min Size)

```csharp
// Enforce minimum window size via AppWindow.Changed
AppWindow.Changed += (sender, args) =>
{
    if (!args.DidSizeChange) return;
    var size = sender.Size;
    var minW = 800;
    var minH = 600;
    var newW = Math.Max(size.Width, minW);
    var newH = Math.Max(size.Height, minH);
    if (newW != size.Width || newH != size.Height)
        sender.Resize(new SizeInt32(newW, newH));
};
```

---

## Error Window Pattern (Dedicated Crash Report Window)

When an unhandled exception occurs, open a **dedicated error window** instead of navigating
to an error page inside the main frame. This keeps the main window intact and gives the user
a clear, separate surface to review and report the error.

```csharp
// App.xaml.cs — global exception handlers
private static Exception? _pendingException;

private void OnWinUIUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
{
    _pendingException = e.Exception ?? new Exception(e.Message);
    OpenErrorWindow();
    e.Handled = true;
}

private void OpenErrorWindow()
{
    _window?.DispatcherQueue.TryEnqueue(() =>
    {
        var errorWindow = new ErrorWindow();
        errorWindow.Activate();
    });
}

public static Exception? ConsumePendingException()
{
    var ex = _pendingException;
    _pendingException = null;
    return ex;
}
```

```csharp
// ErrorWindow.xaml.cs — proportional sizing, system info, repro steps
public sealed partial class ErrorWindow : Window
{
    private string _errorDetail = "";
    private string _systemInfo = "";
    private static string? _cachedSystemInfo;

    public ErrorWindow()
    {
        InitializeComponent();

        AppWindow.Title = "Error Report";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        // Proportional sizing — error windows are medium-sized
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId);
        var workArea = displayArea.WorkArea;
        var width = (int)(workArea.Width * 0.55);
        var height = (int)(workArea.Height * 0.70);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (workArea.Width - width) / 2,
            (workArea.Height - height) / 2));

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        var ex = App.ConsumePendingException();
        if (ex is not null) SetError(ex);

        LoadSystemInfo();
    }
}
```

The error window XAML should include:
- **Header** with error icon and description
- **Exception details card** (scrollable, copyable, `IsTextSelectionEnabled="True"`)
- **System info card** (collapsible by default — use chevron toggle + `Visibility`)
- **Repro steps input** (`TextBox` with `AcceptsReturn="True"`)
- **Action buttons** in a fixed bottom bar: Submit Issue, Restart, Close

---

## Notes

- **Never hard-code pixel sizes for new windows.** Use `DisplayArea.GetFromWindowId()` to get
  the work area and calculate proportions. Users have wildly different screen sizes and DPI.
- `DisplayArea.GetFromWindowId()` requires a `WindowId`. Get it via:
  `WinRT.Interop.WindowNative.GetWindowHandle(this)` →
  `Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd)`.
- Always center windows after resizing with `AppWindow.Move`.
- `AppWindow.Resize` uses **physical pixels**, not logical pixels. The work area values from
  `DisplayArea` are also in physical pixels, so the math is consistent.
- For the main window, `WindowSizeService` may handle save/restore of user preferences —
  proportional sizing is mainly for secondary/tool windows where there is no saved state.
- Each `Window` has its own `DispatcherQueue`. Use it when marshaling calls between windows.
