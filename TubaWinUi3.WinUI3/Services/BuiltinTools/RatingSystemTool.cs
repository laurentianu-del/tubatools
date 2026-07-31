using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class RatingSystemTool : IBuiltinTool
{
	public string Id => "rating-system";
	public string Name => "硬件评分";
	public string Description => "为你的笔记本或台式机硬件打分，查看社区排行榜对比评价。";
	public string Glyph => "\ue735";
	public string Category => "硬件信息";
	public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

	public Task ExecuteAsync(BuiltinToolContext context)
	{
		var window = new Window();
		var page = new RatingSystemPage();
		page.RequestedTheme = ThemeService.CurrentElementTheme;
		window.Content = page;
		BackdropService.ApplyBackdrop(window);
		window.AppWindow.Title = "硬件评分系统";
		try
		{
			var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
			if (displayArea is not null)
			{
				var workArea = displayArea.WorkArea;
				int w = (int)((double)workArea.Width * 0.72);
				int h = (int)((double)workArea.Height * 0.82);
				window.AppWindow.Resize(new SizeInt32(w, h));
				window.AppWindow.Move(new PointInt32(workArea.X + (workArea.Width - w) / 2, workArea.Y + (workArea.Height - h) / 2));
			}
		}
		catch
		{
			window.AppWindow.Resize(new SizeInt32(1000, 700));
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
		tb.ButtonInactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
	}
}
