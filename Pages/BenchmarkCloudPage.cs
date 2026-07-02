using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.System;

namespace TubaWinUi3.Pages;

public sealed class BenchmarkCloudPage : Page, IComponentConnector
{
	private List<BenchmarkReportEntry> _allReports = new List<BenchmarkReportEntry>();

	private List<BenchmarkLeaderboardEntry> _leaderboard = new List<BenchmarkLeaderboardEntry>();

	private List<BenchmarkReportEntry> _myReports = new List<BenchmarkReportEntry>();

	private bool _loaded;

	private Pivot MainPivot;

	private ProgressBar MyHistoryProgress;

	private ScrollViewer MyHistoryArea;

	private StackPanel MyHistoryEmpty;

	private ListView MyHistoryList;

	private WebView2 MyHistoryChart;

	private Button DeleteMyReportBtn;

	private TextBlock MyHistoryLoginHint;

	private ProgressBar SameHwProgress;

	private ListView SameHwList;

	private StackPanel SameHwEmpty;

	private StackPanel SameHwInfo;

	private TextBlock SameHwCpuText;

	private TextBlock SameHwGpuText;

	private ProgressBar CompareProgress;

	private ScrollViewer CompareResultArea;

	private StackPanel CompareEmpty;

	private WebView2 CompareRadarChart;

	private ComboBox CompareMyCombo;

	private ComboBox CompareOtherCombo;

	private Button CompareButton;

	private ProgressBar LeaderboardProgress;

	private ListView LeaderboardList;

	private StackPanel LeaderboardEmpty;

	private TextBlock LeaderboardEmptyText;

	private ComboBox SortByCombo;

	private AutoSuggestBox CpuFilterBox;

	private Button RefreshButton;

	private Button UploadButton;

	private TextBlock ReportCountText;

	private bool _contentLoaded;

	public BenchmarkCloudPage()
	{
		InitializeComponent();
		base.Loaded += OnLoaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (!_loaded)
		{
			_loaded = true;
			await LoadDataAsync();
		}
	}

	private async void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		BenchmarkCloudService.InvalidateCache();
		await LoadDataAsync();
	}

	private async Task LoadDataAsync()
	{
		LeaderboardProgress.Visibility = Visibility.Visible;
		LeaderboardEmpty.Visibility = Visibility.Collapsed;
		LeaderboardList.Visibility = Visibility.Collapsed;
		try
		{
			_allReports = await BenchmarkCloudService.GetAllReportsAsync(CancellationToken.None);
			ReportCountText.Text = $"{_allReports.Count} 份报告";
			RefreshLeaderboard();
			await LoadCompareCombos();
			await LoadSameHardware();
			await LoadMyHistory();
		}
		catch (Exception ex)
		{
			LeaderboardEmpty.Visibility = Visibility.Visible;
			LeaderboardEmptyText.Text = ex.Message;
		}
		finally
		{
			LeaderboardProgress.Visibility = Visibility.Collapsed;
		}
	}

	private void SortByCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_loaded)
		{
			RefreshLeaderboard();
		}
	}

	private void Filter_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
	{
		RefreshLeaderboard();
	}

	private void RefreshLeaderboard()
	{
		string sortBy = SortByCombo.SelectedIndex switch
		{
			1 => "office", 
			2 => "cpu", 
			3 => "gpu", 
			4 => "disk", 
			5 => "browser", 
			_ => "gaming", 
		};
		string text = CpuFilterBox.Text;
		_leaderboard = BenchmarkCloudService.ComputeLeaderboard(_allReports, sortBy, text);
		LeaderboardList.ItemsSource = _leaderboard;
		LeaderboardList.Visibility = ((_leaderboard.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		LeaderboardEmpty.Visibility = ((_leaderboard.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void LeaderboardList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (LeaderboardList.SelectedItem is BenchmarkLeaderboardEntry benchmarkLeaderboardEntry)
		{
			ShowReportDetailDialog(benchmarkLeaderboardEntry.Report);
		}
	}

	private async Task LoadCompareCombos()
	{
		CompareMyCombo.ItemsSource = _allReports;
		CompareOtherCombo.ItemsSource = _allReports;
		if (_myReports.Count > 0)
		{
			CompareMyCombo.SelectedIndex = 0;
		}
		else if (_allReports.Count > 0)
		{
			CompareMyCombo.SelectedIndex = 0;
		}
	}

	private async void CompareButton_Click(object sender, RoutedEventArgs e)
	{
		BenchmarkReportEntry benchmarkReportEntry = CompareMyCombo.SelectedItem as BenchmarkReportEntry;
		BenchmarkReportEntry benchmarkReportEntry2 = CompareOtherCombo.SelectedItem as BenchmarkReportEntry;
		if (benchmarkReportEntry == null || benchmarkReportEntry2 == null)
		{
			await new ContentDialog
			{
				Title = "提示",
				Content = "请选择两个报告进行对比",
				CloseButtonText = "确定",
				XamlRoot = base.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
		}
		else
		{
			CompareEmpty.Visibility = Visibility.Collapsed;
			CompareResultArea.Visibility = Visibility.Visible;
			await RenderRadarChart(benchmarkReportEntry, benchmarkReportEntry2);
		}
	}

	private async Task RenderRadarChart(BenchmarkReportEntry a, BenchmarkReportEntry b)
	{
		await CompareRadarChart.EnsureCoreWebView2Async();
		string htmlContent = BuildRadarChartHtml(a, b);
		CompareRadarChart.NavigateToString(htmlContent);
	}

	private static string BuildRadarChartHtml(BenchmarkReportEntry a, BenchmarkReportEntry b)
	{
		string[] array = new string[9] { "CPU单核", "CPU多核", "GPU渲染", "内存", "硬盘读", "硬盘写", "4K读", "4K写", "浏览器" };
		int[] array2 = new int[9]
		{
			a.CpuSingleCoreScore * 10,
			a.CpuMultiCoreScore,
			a.GpuRenderScore,
			a.MemoryCapacityScore,
			a.DiskSeqReadScore,
			a.DiskSeqWriteScore,
			a.Disk4KReadScore,
			a.Disk4KWriteScore,
			a.BrowserTotalScore
		};
		int[] array3 = new int[9]
		{
			b.CpuSingleCoreScore * 10,
			b.CpuMultiCoreScore,
			b.GpuRenderScore,
			b.MemoryCapacityScore,
			b.DiskSeqReadScore,
			b.DiskSeqWriteScore,
			b.Disk4KReadScore,
			b.Disk4KWriteScore,
			b.BrowserTotalScore
		};
		int num = array.Length;
		int num2 = 200;
		int num3 = 200;
		int num4 = 150;
		int num5 = Math.Max(array2.Max(), array3.Max());
		if (num5 == 0)
		{
			num5 = 1;
		}
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<string> list3 = new List<string>();
		for (int i = 0; i < num; i++)
		{
			double num6 = -Math.PI / 2.0 + Math.PI * 2.0 * (double)i / (double)num;
			double num7 = Math.Cos(num6);
			double num8 = Math.Sin(num6);
			double num9 = (double)array2[i] / (double)num5 * (double)num4;
			double num10 = (double)array3[i] / (double)num5 * (double)num4;
			list.Add($"{(double)num2 + num9 * num7:F1},{(double)num3 + num9 * num8:F1}");
			list2.Add($"{(double)num2 + num10 * num7:F1},{(double)num3 + num10 * num8:F1}");
			double value = (double)num2 + (double)(num4 + 20) * num7;
			double value2 = (double)num3 + (double)(num4 + 20) * num8;
			list3.Add($"<text x='{value:F0}' y='{value2:F0}' text-anchor='middle' dominant-baseline='middle' font-size='10' fill='#5a7a9a'>{array[i]}</text>");
			for (double num11 = 0.25; num11 <= 1.0; num11 += 0.25)
			{
				list3.Add($"<circle cx='{num2}' cy='{num3}' r='{(double)num4 * num11:F0}' fill='none' stroke='#d0dde8' stroke-width='0.5'/>");
			}
			list3.Add($"<line x1='{num2}' y1='{num3}' x2='{(double)num2 + (double)num4 * num7:F1}' y2='{(double)num3 + (double)num4 * num8:F1}' stroke='#d0dde8' stroke-width='0.5'/>");
		}
		return $"<!DOCTYPE html><html><head><meta charset=\"utf-8\">\n<style>body{{font-family:'Segoe UI',sans-serif;margin:0;background:#f8fafd;display:flex;justify-content:center;align-items:center;min-height:360px}}\n.legend{{position:absolute;top:12px;left:16px;font-size:12px;display:flex;gap:16px}}\n.legend-dot{{width:10px;height:10px;border-radius:2px;display:inline-block;margin-right:4px}}\n</style></head><body>\n<div class=\"legend\"><span><span class=\"legend-dot\" style=\"background:rgba(59,125,216,0.5)\"></span>{ShortName(a)}</span><span><span class=\"legend-dot\" style=\"background:rgba(224,123,57,0.5)\"></span>{ShortName(b)}</span></div>\n<svg width='400' height='400' viewBox='0 0 400 400'>\n{string.Join("\n", list3)}\n<polygon points='{string.Join(" ", list)}' fill='rgba(59,125,216,0.25)' stroke='#3b7dd8' stroke-width='1.5'/>\n<polygon points='{string.Join(" ", list2)}' fill='rgba(224,123,57,0.25)' stroke='#e07b39' stroke-width='1.5'/>\n</svg></body></html>";
		static string ShortName(BenchmarkReportEntry r)
		{
			return r.Author + " · " + r.CpuName.Split(' ').FirstOrDefault();
		}
	}

	private async Task LoadSameHardware()
	{
		SameHwProgress.Visibility = Visibility.Visible;
		try
		{
			string text = "";
			string text2 = "";
			if (_myReports.Count > 0)
			{
				text = _myReports[0].CpuName;
				text2 = _myReports[0].GpuName;
			}
			else
			{
				List<PerformanceBenchmarkResult> list = PerformanceBenchmarkService.LoadHistory();
				if (list.Count > 0)
				{
					text = list[0].CpuName;
					text2 = list[0].GpuName;
				}
			}
			SameHwCpuText.Text = "CPU: " + text;
			SameHwGpuText.Text = "GPU: " + text2;
			if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(text2))
			{
				SameHwList.Visibility = Visibility.Collapsed;
				SameHwEmpty.Visibility = Visibility.Visible;
				return;
			}
			List<BenchmarkLeaderboardEntry> list2 = BenchmarkCloudService.ComputeSameHardwareLeaderboard(_allReports, text, text2);
			SameHwList.ItemsSource = list2;
			SameHwList.Visibility = ((list2.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
			SameHwEmpty.Visibility = ((list2.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		}
		finally
		{
			SameHwProgress.Visibility = Visibility.Collapsed;
		}
	}

	private async Task LoadMyHistory()
	{
		if (!GitHubAuthService.IsLoggedIn)
		{
			MyHistoryLoginHint.Text = "请先登录 GitHub 查看你的上传记录";
			MyHistoryArea.Visibility = Visibility.Collapsed;
			MyHistoryEmpty.Visibility = Visibility.Visible;
			return;
		}
		MyHistoryProgress.Visibility = Visibility.Visible;
		try
		{
			_myReports = await BenchmarkCloudService.GetMyReportsAsync(CancellationToken.None);
			MyHistoryLoginHint.Text = $"已登录，共 {_myReports.Count} 条上传记录";
			if (_myReports.Count == 0)
			{
				MyHistoryArea.Visibility = Visibility.Collapsed;
				MyHistoryEmpty.Visibility = Visibility.Visible;
				return;
			}
			MyHistoryArea.Visibility = Visibility.Visible;
			MyHistoryEmpty.Visibility = Visibility.Collapsed;
			MyHistoryList.ItemsSource = _myReports;
			await RenderHistoryChart(_myReports);
		}
		finally
		{
			MyHistoryProgress.Visibility = Visibility.Collapsed;
		}
	}

	private void MyHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		DeleteMyReportBtn.Visibility = ((MyHistoryList.SelectedItem == null) ? Visibility.Collapsed : Visibility.Visible);
	}

	private async void DeleteMyReportBtn_Click(object sender, RoutedEventArgs e)
	{
		if (MyHistoryList.SelectedItem is BenchmarkReportEntry report)
		{
			await DeleteReportAsync(report);
		}
	}

	private async Task RenderHistoryChart(List<BenchmarkReportEntry> reports)
	{
		await MyHistoryChart.EnsureCoreWebView2Async();
		string htmlContent = BuildHistoryChartHtml(reports);
		MyHistoryChart.NavigateToString(htmlContent);
	}

	private static string BuildHistoryChartHtml(List<BenchmarkReportEntry> reports)
	{
		List<BenchmarkReportEntry> list = reports.OrderBy((BenchmarkReportEntry r) => r.SubmittedAt).ToList();
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">\n<style>body{font-family:'Segoe UI',sans-serif;margin:16px;background:#f8fafd}\nsvg text{font-size:10px;fill:#5a7a9a}\n.line-gaming{fill:none;stroke:#3b7dd8;stroke-width:2}\n.line-office{fill:none;stroke:#e07b39;stroke-width:2}\n.dot-gaming{fill:#3b7dd8}\n.dot-office{fill:#e07b39}\n.legend{display:flex;gap:16px;margin-bottom:8px;font-size:12px}\n.legend-dot{width:10px;height:10px;border-radius:2px;display:inline-block;margin-right:4px}\n</style></head><body>\n<div class=\"legend\"><span><span class=\"legend-dot\" style=\"background:#3b7dd8\"></span>游戏性能</span><span><span class=\"legend-dot\" style=\"background:#e07b39\"></span>办公性能</span></div>\n<svg width='100%' height='260' viewBox='0 0 600 260'>");
		int num = 560;
		int num2 = 220;
		int num3 = 40;
		int num4 = 20;
		int num5 = Math.Max(list.Max((BenchmarkReportEntry r) => r.GamingScore), list.Max((BenchmarkReportEntry r) => r.OfficeScore));
		if (num5 == 0)
		{
			num5 = 100;
		}
		int num6 = (int)((double)num5 * 1.1);
		for (int num7 = 0; num7 <= 4; num7++)
		{
			double num8 = (double)(num4 + num2) - (double)num7 / 4.0 * (double)num2;
			int value = (int)((double)num7 / 4.0 * (double)num6);
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(67, 4, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(num3);
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(num8, "F0");
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(num3 + num);
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(num8, "F0");
			handler.AppendLiteral("' stroke='#e8eef4' stroke-width='0.5'/>");
			stringBuilder3.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(41, 3, stringBuilder2);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(num3 - 4);
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(num8 + 3.0, "F0");
			handler.AppendLiteral("' text-anchor='end'>");
			handler.AppendFormatted(value);
			handler.AppendLiteral("</text>");
			stringBuilder4.AppendLine(ref handler);
		}
		int count = list.Count;
		for (int num9 = 0; num9 < count; num9++)
		{
			double value2 = (double)num3 + (double)num9 / (double)Math.Max(count - 1, 1) * (double)num;
			double value3 = (double)(num4 + num2) - (double)list[num9].GamingScore / (double)num6 * (double)num2;
			double value4 = (double)(num4 + num2) - (double)list[num9].OfficeScore / (double)num6 * (double)num2;
			StringBuilder stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler;
			if (num9 > 0)
			{
				double value5 = (double)num3 + (double)(num9 - 1) / (double)Math.Max(count - 1, 1) * (double)num;
				double value6 = (double)(num4 + num2) - (double)list[num9 - 1].GamingScore / (double)num6 * (double)num2;
				double value7 = (double)(num4 + num2) - (double)list[num9 - 1].OfficeScore / (double)num6 * (double)num2;
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder5 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(51, 4, stringBuilder2);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(value5, "F1");
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(value6, "F1");
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(value2, "F1");
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(value3, "F1");
				handler.AppendLiteral("' class='line-gaming'/>");
				stringBuilder5.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder6 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(51, 4, stringBuilder2);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(value5, "F1");
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(value7, "F1");
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(value2, "F1");
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(value4, "F1");
				handler.AppendLiteral("' class='line-office'/>");
				stringBuilder6.AppendLine(ref handler);
			}
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(46, 2, stringBuilder2);
			handler.AppendLiteral("<circle cx='");
			handler.AppendFormatted(value2, "F1");
			handler.AppendLiteral("' cy='");
			handler.AppendFormatted(value3, "F1");
			handler.AppendLiteral("' r='3' class='dot-gaming'/>");
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(46, 2, stringBuilder2);
			handler.AppendLiteral("<circle cx='");
			handler.AppendFormatted(value2, "F1");
			handler.AppendLiteral("' cy='");
			handler.AppendFormatted(value4, "F1");
			handler.AppendLiteral("' r='3' class='dot-office'/>");
			stringBuilder8.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(44, 3, stringBuilder2);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(value2, "F1");
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(num4 + num2 + 14, "F0");
			handler.AppendLiteral("' text-anchor='middle'>");
			handler.AppendFormatted(list[num9].SubmittedAt.LocalDateTime, "MM/dd");
			handler.AppendLiteral("</text>");
			stringBuilder9.AppendLine(ref handler);
		}
		stringBuilder.AppendLine("</svg></body></html>");
		return stringBuilder.ToString();
	}

	private async void UploadButton_Click(object sender, RoutedEventArgs e)
	{
		List<PerformanceBenchmarkResult> localHistory = PerformanceBenchmarkService.LoadHistory();
		if (localHistory.Count == 0)
		{
			await new ContentDialog
			{
				Title = "无测试报告",
				Content = "请先运行一次性能测试，再上传报告。",
				CloseButtonText = "确定",
				XamlRoot = base.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
			return;
		}
		if (!GitHubAuthService.IsLoggedIn)
		{
			try
			{
				await GitHubAuthService.EnsureAuthenticatedAsync(base.XamlRoot, CancellationToken.None);
			}
			catch
			{
				await new ContentDialog
				{
					Title = "需要登录",
					Content = "上传报告需要 GitHub 账号，请先在设置中登录。",
					CloseButtonText = "确定",
					XamlRoot = base.XamlRoot,
					RequestedTheme = ThemeService.CurrentElementTheme
				}.ShowAsync();
				return;
			}
		}
		PerformanceBenchmarkResult latest = localHistory[0];
		if (await new ContentDialog
		{
			Title = "上传测试报告",
			Content = $"将上传最新的测试报告：\n\nCPU: {latest.CpuName}\nGPU: {latest.GpuName}\n游戏: {latest.GamingScore} ({latest.GamingGrade})\n办公: {latest.OfficeScore} ({latest.OfficeGrade})\n\n报告将通过 PR 提交到社区仓库。",
			PrimaryButtonText = "上传",
			CloseButtonText = "取消",
			XamlRoot = base.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		}.ShowAsync() != ContentDialogResult.Primary)
		{
			return;
		}
		ContentDialog progressDlg = new ContentDialog
		{
			Title = "正在上传",
			Content = new ProgressBar
			{
				IsIndeterminate = true
			},
			XamlRoot = base.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		progressDlg.ShowAsync();
		try
		{
			Progress<string> progress = new Progress<string>(delegate(string msg)
			{
				base.DispatcherQueue.TryEnqueue(delegate
				{
					progressDlg.Content = new StackPanel
					{
						Spacing = 8.0,
						Children = 
						{
							(UIElement)new TextBlock
							{
								Text = msg
							},
							(UIElement)new ProgressBar
							{
								IsIndeterminate = true
							}
						}
					};
				});
			});
			string prUrl = await BenchmarkCloudService.UploadReportAsync(latest, progress, CancellationToken.None);
			progressDlg.Hide();
			if (await new ContentDialog
			{
				Title = "上传成功",
				Content = "报告已通过 PR 提交，合并后将出现在排行榜。\n\nPR 链接：" + prUrl,
				PrimaryButtonText = "打开 PR",
				CloseButtonText = "关闭",
				XamlRoot = base.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync() == ContentDialogResult.Primary)
			{
				await Launcher.LaunchUriAsync(new Uri(prUrl));
			}
			BenchmarkCloudService.InvalidateCache();
			await LoadDataAsync();
		}
		catch (Exception ex)
		{
			progressDlg.Hide();
			await new ContentDialog
			{
				Title = "上传失败",
				Content = ex.Message,
				CloseButtonText = "确定",
				XamlRoot = base.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
		}
	}

	private async void ShowReportDetailDialog(BenchmarkReportEntry report)
	{
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 8.0
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "CPU: " + report.CpuName,
			Opacity = 0.8
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "GPU: " + report.GpuName,
			Opacity = 0.8
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "主板: " + report.MotherboardName,
			Opacity = 0.8
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "内存: " + report.MemoryInfo,
			Opacity = 0.8
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "硬盘: " + report.DiskInfo,
			Opacity = 0.8
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "显示器: " + report.DisplayInfo,
			Opacity = 0.8
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "OS: " + report.OsName,
			Opacity = 0.8
		});
		stackPanel.Children.Add(new Border
		{
			Height = 1.0,
			Background = new SolidColorBrush(ColorHelper.FromArgb(byte.MaxValue, 208, 221, 232)),
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"游戏性能: {report.GamingScore} ({report.GamingGrade})",
			FontWeight = FontWeights.SemiBold
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"办公性能: {report.OfficeScore} ({report.OfficeGrade})",
			FontWeight = FontWeights.SemiBold
		});
		stackPanel.Children.Add(new Border
		{
			Height = 1.0,
			Background = new SolidColorBrush(ColorHelper.FromArgb(byte.MaxValue, 208, 221, 232)),
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"CPU单核: {report.CpuSingleCoreScore}",
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"CPU多核: {report.CpuMultiCoreScore}",
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"GPU渲染: {report.GpuRenderScore}",
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"内存: {report.MemoryCapacityScore}",
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"硬盘顺序读: {report.DiskSeqReadScore}",
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"硬盘顺序写: {report.DiskSeqWriteScore}",
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"硬盘4K读: {report.Disk4KReadScore}",
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"硬盘4K写: {report.Disk4KWriteScore}",
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"浏览器: {report.BrowserTotalScore}",
			Opacity = 0.7
		});
		bool num = GitHubAuthService.IsLoggedIn && GitHubAuthService.GetToken() != null && _myReports.Any((BenchmarkReportEntry r) => r.Id == report.Id);
		ContentDialog contentDialog = new ContentDialog
		{
			Title = "@" + report.Author + " 的测试报告",
			Content = new ScrollViewer
			{
				Content = stackPanel,
				MaxHeight = 500.0
			},
			CloseButtonText = "关闭",
			XamlRoot = base.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		if (num)
		{
			contentDialog.SecondaryButtonText = "删除此报告";
			if (await contentDialog.ShowAsync() == ContentDialogResult.Secondary)
			{
				await DeleteReportAsync(report);
			}
		}
		else
		{
			await contentDialog.ShowAsync();
		}
	}

	private async Task DeleteReportAsync(BenchmarkReportEntry report)
	{
		if (await new ContentDialog
		{
			Title = "确认删除",
			Content = $"确定要删除这份报告吗？\n\nCPU: {report.CpuName}\nGPU: {report.GpuName}\n游戏: {report.GamingScore} 办公: {report.OfficeScore}\n\n将通过 PR 删除，合并后生效。",
			PrimaryButtonText = "删除",
			CloseButtonText = "取消",
			XamlRoot = base.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		}.ShowAsync() != ContentDialogResult.Primary)
		{
			return;
		}
		ContentDialog progressDlg = new ContentDialog
		{
			Title = "正在删除",
			Content = new ProgressBar
			{
				IsIndeterminate = true
			},
			XamlRoot = base.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		progressDlg.ShowAsync();
		try
		{
			Progress<string> progress = new Progress<string>(delegate(string msg)
			{
				base.DispatcherQueue.TryEnqueue(delegate
				{
					progressDlg.Content = new StackPanel
					{
						Spacing = 8.0,
						Children = 
						{
							(UIElement)new TextBlock
							{
								Text = msg
							},
							(UIElement)new ProgressBar
							{
								IsIndeterminate = true
							}
						}
					};
				});
			});
			string prUrl = await BenchmarkCloudService.DeleteReportAsync(report, progress, CancellationToken.None);
			progressDlg.Hide();
			if (await new ContentDialog
			{
				Title = "删除请求已提交",
				Content = "报告删除 PR 已创建，合并后将从排行榜移除。\n\nPR 链接：" + prUrl,
				PrimaryButtonText = "打开 PR",
				CloseButtonText = "关闭",
				XamlRoot = base.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync() == ContentDialogResult.Primary)
			{
				await Launcher.LaunchUriAsync(new Uri(prUrl));
			}
			BenchmarkCloudService.InvalidateCache();
			await LoadDataAsync();
		}
		catch (Exception ex)
		{
			progressDlg.Hide();
			await new ContentDialog
			{
				Title = "删除失败",
				Content = ex.Message,
				CloseButtonText = "确定",
				XamlRoot = base.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
		}
	}

	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Pages/BenchmarkCloudPage.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	public void Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 2:
			MainPivot = (Pivot)target;
			break;
		case 3:
			MyHistoryProgress = (ProgressBar)target;
			break;
		case 4:
			MyHistoryArea = (ScrollViewer)target;
			break;
		case 5:
			MyHistoryEmpty = (StackPanel)target;
			break;
		case 6:
			MyHistoryList = (ListView)target;
			MyHistoryList.SelectionChanged += MyHistoryList_SelectionChanged;
			break;
		case 8:
			MyHistoryChart = (WebView2)target;
			break;
		case 9:
			DeleteMyReportBtn = (Button)target;
			DeleteMyReportBtn.Click += DeleteMyReportBtn_Click;
			break;
		case 10:
			MyHistoryLoginHint = (TextBlock)target;
			break;
		case 11:
			SameHwProgress = (ProgressBar)target;
			break;
		case 12:
			SameHwList = (ListView)target;
			break;
		case 13:
			SameHwEmpty = (StackPanel)target;
			break;
		case 15:
			SameHwInfo = (StackPanel)target;
			break;
		case 16:
			SameHwCpuText = (TextBlock)target;
			break;
		case 17:
			SameHwGpuText = (TextBlock)target;
			break;
		case 18:
			CompareProgress = (ProgressBar)target;
			break;
		case 19:
			CompareResultArea = (ScrollViewer)target;
			break;
		case 20:
			CompareEmpty = (StackPanel)target;
			break;
		case 21:
			CompareRadarChart = (WebView2)target;
			break;
		case 22:
			CompareMyCombo = (ComboBox)target;
			break;
		case 23:
			CompareOtherCombo = (ComboBox)target;
			break;
		case 24:
			CompareButton = (Button)target;
			CompareButton.Click += CompareButton_Click;
			break;
		case 25:
			LeaderboardProgress = (ProgressBar)target;
			break;
		case 26:
			LeaderboardList = (ListView)target;
			LeaderboardList.SelectionChanged += LeaderboardList_SelectionChanged;
			break;
		case 27:
			LeaderboardEmpty = (StackPanel)target;
			break;
		case 28:
			LeaderboardEmptyText = (TextBlock)target;
			break;
		case 31:
			SortByCombo = (ComboBox)target;
			SortByCombo.SelectionChanged += SortByCombo_SelectionChanged;
			break;
		case 32:
			CpuFilterBox = (AutoSuggestBox)target;
			CpuFilterBox.QuerySubmitted += Filter_QuerySubmitted;
			break;
		case 33:
			RefreshButton = (Button)target;
			RefreshButton.Click += RefreshButton_Click;
			break;
		case 34:
			UploadButton = (Button)target;
			UploadButton.Click += UploadButton_Click;
			break;
		case 35:
			ReportCountText = (TextBlock)target;
			break;
		}
		_contentLoaded = true;
	}

	public IComponentConnector GetBindingConnector(int connectionId, object target)
	{
		return null;
	}
}
