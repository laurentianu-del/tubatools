using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class CommunityToolBuiltinTool : IBuiltinTool
{
	public string Id => "community-tools";
	public string Name => "社区工具";
	public string Description => "来自社区贡献的工具插件，下载安装即可使用。支持提交和删除工具。";
	public string Glyph => "\ue774";
	public string Category => "社区";
	public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

	public Task ExecuteAsync(BuiltinToolContext context)
	{
		var window = new Window();
		var page = new CommunityToolsPage();
		page.RequestedTheme = ThemeService.CurrentElementTheme;
		window.Content = page;
		BackdropService.ApplyBackdrop(window);
		window.AppWindow.Title = "社区工具";
		window.AppWindow.Resize(new SizeInt32(1100, 780));
		try
		{
			var mainPos = App.MainWindow?.AppWindow.Position;
			if (mainPos.HasValue)
			{
				window.AppWindow.Move(new PointInt32(mainPos.Value.X + 50, mainPos.Value.Y + 50));
			}
		}
		catch
		{
		}
		window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
		window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
		ApplyTitleBarTheme(window);
		window.Activate();
		return Task.CompletedTask;
	}

	private static void ApplyTitleBarTheme(Window window)
	{
		var tb = window.AppWindow.TitleBar;
		var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
		             (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

		if (isDark)
		{
			tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
			tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
			tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
			tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
			tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
			tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);
			tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
			tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
		}
		else
		{
			tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
			tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
			tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
			tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
			tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
			tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
			tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
			tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
		}
		tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
	}
}
