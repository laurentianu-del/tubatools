using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public static class TrayIconService
{
    private static NotifyIcon? _notifyIcon;
    private static bool _isInitialized;

    public static bool IsVisible => _notifyIcon?.Visible == true;

    public static void Show()
    {
        if (_isInitialized && _notifyIcon is not null)
        {
            _notifyIcon.Visible = true;
            return;
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = GetIcon(),
            Text = "防晕3D运行中",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("停止并退出", null, (_, _) => StopAndExit());
        _notifyIcon.ContextMenuStrip = contextMenu;

        _notifyIcon.DoubleClick += (_, _) => StopAndExit();

        _isInitialized = true;
    }

    public static void Hide()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }
    }

    public static void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
            _isInitialized = false;
        }
    }

    private static void StopAndExit()
    {
        AntiMotionSicknessOverlay.CloseOverlay();
        Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
        System.Diagnostics.Process.GetCurrentProcess().Kill();
    }

    private static Icon GetIcon()
    {
        var size = new Size(16, 16);
        using var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var pen = new Pen(Color.LimeGreen, 2);
        g.DrawLine(pen, 4, 8, 12, 8);
        g.DrawLine(pen, 8, 4, 8, 12);
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
