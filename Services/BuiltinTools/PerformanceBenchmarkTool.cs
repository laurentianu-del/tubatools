using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class PerformanceBenchmarkTool : IBuiltinTool
{
	public string Id => "performance-benchmark";
	public string Name => "性能测试";
	public string Description => "全面测试 CPU/GPU/内存/硬盘/浏览器性能，按比例计算游戏与办公性能评分，导出专业 PDF 报告。";
	public string Glyph => "\ue9d9";
	public string Category => "硬件信息";
	public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

	public Task ExecuteAsync(BuiltinToolContext context)
	{
		try
		{
			var window = new Window();
			PerformanceBenchmarkPage page;
			try
			{
				page = new PerformanceBenchmarkPage(window);
			}
			catch (Exception ex)
			{
				File.WriteAllText(Path.Combine(Path.GetTempPath(), "bench_tool_error.log"), $"Page ctor failed:\n{ex}\n\n---Inner---\n{ex.InnerException}");
				throw;
			}
			page.RequestedTheme = ThemeService.CurrentElementTheme;
			window.Content = page;
			BackdropService.ApplyBackdrop(window);
			window.AppWindow.Title = "性能测试";
			try
			{
				var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
				if (displayArea is not null)
				{
					var workArea = displayArea.WorkArea;
					int w = (int)((double)workArea.Width * 0.78);
					int h = (int)((double)workArea.Height * 0.85);
					window.AppWindow.Resize(new SizeInt32(w, h));
					window.AppWindow.Move(new PointInt32(workArea.X + (workArea.Width - w) / 2, workArea.Y + (workArea.Height - h) / 2));
				}
			}
			catch
			{
				window.AppWindow.Resize(new SizeInt32(1100, 780));
			}
			window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
			window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
			ApplyTitleBarTheme(window);
			window.Activate();
			return Task.CompletedTask;
		}
		catch (Exception ex2)
		{
			File.WriteAllText(Path.Combine(Path.GetTempPath(), "bench_tool_error2.log"), $"ExecuteAsync failed:\n{ex2}\n\n---Inner---\n{ex2.InnerException}");
			throw;
		}
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
