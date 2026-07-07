using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed class PerformanceBenchmarkPage : Page
{
	private readonly Window _window;
	private CancellationTokenSource _cts = new();
	private PerformanceBenchmarkResult? _result;
	private bool _isRunning;
	private TextBlock _gamingScoreText;
	private TextBlock _gamingGradeText;
	private ProgressBar _gamingBar;
	private TextBlock _officeScoreText;
	private TextBlock _officeGradeText;
	private ProgressBar _officeBar;
	private TextBlock _cpuSingleScoreText;
	private TextBlock _cpuMultiScoreText;
	private TextBlock _cpuLatencyScoreText;
	private Border _latencyGridContainer;
	private Image _latencyHeatmapImage;
	private string? _latencyHeatmapPath;
	private TextBlock _gpuRenderScoreText;
	private TextBlock _gpuFurMarkScoreText;
	private TextBlock _gpuAvgFpsText;
	private TextBlock _gpuMinFpsText;
	private TextBlock _gpuMaxFpsText;
	private TextBlock _memCapacityText;
	private TextBlock _diskSeqReadScoreText;
	private TextBlock _diskSeqWriteScoreText;
	private TextBlock _disk4KReadScoreText;
	private TextBlock _disk4KWriteScoreText;
	private TextBlock _diskSeqReadDetailText;
	private TextBlock _diskSeqWriteDetailText;
	private TextBlock _disk4KReadDetailText;
	private TextBlock _disk4KWriteDetailText;
	private TextBlock _diskTempText;
	private TextBlock _brJsScoreText;
	private TextBlock _brJsDetailText;
	private TextBlock _brDomScoreText;
	private TextBlock _brDomDetailText;
	private TextBlock _brCardScoreText;
	private TextBlock _brCardDetailText;
	private TextBlock _brCssScoreText;
	private TextBlock _brCssDetailText;
	private TextBlock _brLayoutScoreText;
	private TextBlock _brLayoutDetailText;
	private TextBlock _brEventScoreText;
	private TextBlock _brEventDetailText;
	private Button _startBtn;
	private Button _stopBtn;
	private Button _exportBtn;
	private Button _historyBtn;
	private Button _uploadBtn;
	private Button _rankingBtn;
	private ProgressBar _globalProgress;
	private TextBlock _statusText;
	private CheckBox _chkCpu;
	private CheckBox _chkGpu;
	private CheckBox _chkMem;
	private CheckBox _chkDisk;
	private CheckBox _chkBrowser;

	private static readonly Color AccentBlue = Color.FromArgb(byte.MaxValue, 0, 99, 177);
	private static readonly Color ColorS = Color.FromArgb(byte.MaxValue, 74, 222, 128);
	private static readonly Color ColorAPlus = Color.FromArgb(byte.MaxValue, 34, 197, 94);
	private static readonly Color ColorA = Color.FromArgb(byte.MaxValue, 0, 99, 177);
	private static readonly Color ColorBPlus = Color.FromArgb(byte.MaxValue, 251, 191, 36);
	private static readonly Color ColorB = Color.FromArgb(byte.MaxValue, 251, 146, 60);
	private static readonly Color ColorC = Color.FromArgb(byte.MaxValue, 248, 113, 113);
	private static readonly Color ColorD = Color.FromArgb(byte.MaxValue, 220, 38, 38);

	public PerformanceBenchmarkPage(Window window)
	{
		_window = window;
		base.Content = BuildUI();
	}

	private ScrollViewer BuildUI()
	{
		bool isDark;
		SolidColorBrush solidColorBrush;
		if (ThemeService.CurrentTheme == AppTheme.Dark)
		{
			isDark = true;
		}
		else if (ThemeService.CurrentTheme == AppTheme.Default)
		{
			isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
		}
		else
		{
			isDark = false;
		}

		solidColorBrush = isDark
			? new SolidColorBrush(Color.FromArgb(byte.MaxValue, 45, 45, 45))
			: new SolidColorBrush(Color.FromArgb(byte.MaxValue, 249, 249, 249));
		SolidColorBrush cardBg = solidColorBrush;
		Color borderColor = isDark ? Color.FromArgb(byte.MaxValue, 60, 60, 60) : Color.FromArgb(byte.MaxValue, 229, 229, 229);
		Grid grid = new()
		{
			RowSpacing = 0.0,
			Padding = new Thickness(28.0, 48.0, 28.0, 0.0)
		};
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		Grid grid2 = BuildTopCards(cardBg, borderColor);
		grid.Children.Add(grid2);
		Grid.SetRow(grid2, 0);
		Grid grid3 = new()
		{
			ColumnSpacing = 12.0,
			RowSpacing = 12.0,
			Padding = new Thickness(0.0, 12.0, 0.0, 12.0)
		};
		grid3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid3.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		grid3.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		grid3.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		Border border = BuildSection("CPU 性能", "\ueea1", BuildCpuContent(cardBg, borderColor), cardBg, borderColor);
		grid3.Children.Add(border);
		Grid.SetRow(border, 0);
		Grid.SetColumn(border, 0);
		Border border2 = BuildSection("GPU 性能", "\ue950", BuildGpuContent(cardBg, borderColor), cardBg, borderColor);
		grid3.Children.Add(border2);
		Grid.SetRow(border2, 0);
		Grid.SetColumn(border2, 1);
		Border border3 = BuildSection("内存性能", "\ue90f", BuildMemoryContent(cardBg, borderColor), cardBg, borderColor);
		grid3.Children.Add(border3);
		Grid.SetRow(border3, 1);
		Grid.SetColumn(border3, 0);
		Border border4 = BuildSection("硬盘性能", "\ueda2", BuildDiskContent(cardBg, borderColor), cardBg, borderColor);
		grid3.Children.Add(border4);
		Grid.SetRow(border4, 1);
		Grid.SetColumn(border4, 1);
		Border border5 = BuildSection("浏览器流畅度", "\ue774", BuildBrowserContent(cardBg, borderColor), cardBg, borderColor);
		grid3.Children.Add(border5);
		Grid.SetRow(border5, 2);
		Grid.SetColumn(border5, 0);
		Grid.SetColumnSpan(border5, 2);
		ScrollViewer scrollViewer = new()
		{
			Content = grid3,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollMode = ScrollMode.Disabled
		};
		grid.Children.Add(scrollViewer);
		Grid.SetRow(scrollViewer, 1);
		StackPanel stackPanel = BuildControlBar();
		grid.Children.Add(stackPanel);
		Grid.SetRow(stackPanel, 2);
		return new ScrollViewer
		{
			Content = grid,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollMode = ScrollMode.Disabled
		};
	}

	private Grid BuildTopCards(Brush cardBg, Color borderColor)
	{
		Grid obj = new()
		{
			ColumnSpacing = 12.0,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) },
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) }
			}
		};
		Border border = new()
		{
			Background = cardBg,
			BorderBrush = new SolidColorBrush(borderColor),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(20.0, 16.0, 20.0, 16.0),
			Child = BuildScoreCard("游戏性能", out _gamingScoreText, out _gamingGradeText, out _gamingBar)
		};
		obj.Children.Add(border);
		Grid.SetColumn(border, 0);
		Border border2 = new()
		{
			Background = cardBg,
			BorderBrush = new SolidColorBrush(borderColor),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(20.0, 16.0, 20.0, 16.0),
			Child = BuildScoreCard("办公性能", out _officeScoreText, out _officeGradeText, out _officeBar)
		};
		obj.Children.Add(border2);
		Grid.SetColumn(border2, 1);
		return obj;
	}

	private StackPanel BuildScoreCard(string label, out TextBlock scoreText, out TextBlock gradeText, out ProgressBar bar)
	{
		TextBlock item = new()
		{
			Text = label,
			FontSize = 13.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		};
		scoreText = new TextBlock
		{
			Text = "—",
			FontSize = 36.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		gradeText = new TextBlock
		{
			Text = "",
			FontSize = 16.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = new SolidColorBrush(ThemeColors.DimText),
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		bar = new ProgressBar
		{
			Value = 0.0,
			Maximum = 100.0,
			Height = 6.0
		};
		StackPanel stackPanel = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4.0
		};
		stackPanel.Children.Add(scoreText);
		stackPanel.Children.Add(gradeText);
		return new StackPanel
		{
			Spacing = 6.0,
			Children =
			{
				(UIElement)item,
				(UIElement)stackPanel,
				(UIElement)bar
			}
		};
	}

	private Border BuildSection(string title, string glyph, Panel content, Brush cardBg, Color borderColor)
	{
		StackPanel stackPanel = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel.Children.Add(new FontIcon
		{
			Glyph = glyph,
			FontSize = 16.0,
			Foreground = new SolidColorBrush(AccentBlue)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 15.0,
			FontWeight = FontWeights.SemiBold
		});
		StackPanel stackPanel2 = new() { Spacing = 8.0 };
		stackPanel2.Children.Add(stackPanel);
		stackPanel2.Children.Add(content);
		return new Border
		{
			Background = cardBg,
			BorderBrush = new SolidColorBrush(borderColor),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(16.0, 12.0, 16.0, 12.0),
			Child = stackPanel2
		};
	}

	private Grid BuildScoreRow(string label, out TextBlock scoreText)
	{
		Grid obj = new()
		{
			ColumnSpacing = 8.0,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(100.0) },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Auto }
			}
		};
		TextBlock textBlock = new()
		{
			Text = label,
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		obj.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		scoreText = new TextBlock
		{
			Text = "—",
			FontSize = 13.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		obj.Children.Add(scoreText);
		Grid.SetColumn(scoreText, 1);
		return obj;
	}

	private StackPanel BuildCpuContent(Brush cardBg, Color borderColor)
	{
		StackPanel obj = new()
		{
			Spacing = 6.0,
			Children =
			{
				(UIElement)BuildScoreRow("单核", out _cpuSingleScoreText),
				(UIElement)BuildScoreRow("多核", out _cpuMultiScoreText),
				(UIElement)BuildScoreRow("核间延迟", out _cpuLatencyScoreText)
			}
		};
		_latencyHeatmapImage = new Image
		{
			MaxHeight = 400.0,
			Stretch = Stretch.Uniform,
			HorizontalAlignment = HorizontalAlignment.Center,
			Visibility = Visibility.Collapsed
		};
		_latencyGridContainer = new Border
		{
			Visibility = Visibility.Collapsed,
			Padding = new Thickness(8.0),
			CornerRadius = new CornerRadius(6.0),
			Background = cardBg,
			Child = _latencyHeatmapImage
		};
		obj.Children.Add(_latencyGridContainer);
		return obj;
	}

	private StackPanel BuildGpuContent(Brush cardBg, Color borderColor)
	{
		return new StackPanel
		{
			Spacing = 6.0,
			Children =
			{
				(UIElement)BuildScoreRow("渲染性能", out _gpuRenderScoreText),
				(UIElement)BuildDetailRow("FurMark分数", out _gpuFurMarkScoreText, out _gpuAvgFpsText),
				(UIElement)BuildDetailRow("FPS范围", out _gpuMinFpsText, out _gpuMaxFpsText)
			}
		};
	}

	private StackPanel BuildMemoryContent(Brush cardBg, Color borderColor)
	{
		StackPanel obj = new() { Spacing = 6.0 };
		Grid grid = new() { ColumnSpacing = 8.0 };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100.0) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		TextBlock textBlock = new()
		{
			Text = "容量",
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		_memCapacityText = new TextBlock
		{
			Text = "—",
			FontSize = 12.0,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		grid.Children.Add(_memCapacityText);
		Grid.SetColumn(_memCapacityText, 1);
		obj.Children.Add(grid);
		return obj;
	}

	private Grid BuildDetailRow(string label, out TextBlock scoreText, out TextBlock detailText)
	{
		Grid obj = new()
		{
			ColumnSpacing = 8.0,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(100.0) },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Auto }
			}
		};
		TextBlock textBlock = new()
		{
			Text = label,
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		obj.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		scoreText = new TextBlock
		{
			Text = "—",
			FontSize = 13.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		obj.Children.Add(scoreText);
		Grid.SetColumn(scoreText, 1);
		detailText = new TextBlock
		{
			Text = "",
			FontSize = 11.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		obj.Children.Add(detailText);
		Grid.SetColumn(detailText, 2);
		return obj;
	}

	private StackPanel BuildDiskContent(Brush cardBg, Color borderColor)
	{
		StackPanel obj = new()
		{
			Spacing = 6.0,
			Children =
			{
				(UIElement)BuildDetailRow("顺序读取", out _diskSeqReadScoreText, out _diskSeqReadDetailText),
				(UIElement)BuildDetailRow("顺序写入", out _diskSeqWriteScoreText, out _diskSeqWriteDetailText),
				(UIElement)BuildDetailRow("4K随机读", out _disk4KReadScoreText, out _disk4KReadDetailText),
				(UIElement)BuildDetailRow("4K随机写", out _disk4KWriteScoreText, out _disk4KWriteDetailText)
			}
		};
		Grid grid = new() { ColumnSpacing = 8.0 };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100.0) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		TextBlock textBlock = new()
		{
			Text = "温度",
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		_diskTempText = new TextBlock
		{
			Text = "—",
			FontSize = 12.0,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		grid.Children.Add(_diskTempText);
		Grid.SetColumn(_diskTempText, 1);
		obj.Children.Add(grid);
		return obj;
	}

	private StackPanel BuildBrowserContent(Brush cardBg, Color borderColor)
	{
		StackPanel stackPanel = new() { Spacing = 6.0 };
		stackPanel.Children.Add(BuildDetailRow("JS 引擎", out _brJsScoreText, out _brJsDetailText));
		stackPanel.Children.Add(BuildDetailRow("DOM 表格", out _brDomScoreText, out _brDomDetailText));
		stackPanel.Children.Add(BuildDetailRow("DOM 卡片", out _brCardScoreText, out _brCardDetailText));
		StackPanel stackPanel2 = new() { Spacing = 6.0 };
		stackPanel2.Children.Add(BuildDetailRow("CSS 动画", out _brCssScoreText, out _brCssDetailText));
		stackPanel2.Children.Add(BuildDetailRow("布局重排", out _brLayoutScoreText, out _brLayoutDetailText));
		stackPanel2.Children.Add(BuildDetailRow("事件处理", out _brEventScoreText, out _brEventDetailText));
		Grid grid = new() { ColumnSpacing = 24.0 };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid.Children.Add(stackPanel);
		Grid.SetColumn(stackPanel, 0);
		grid.Children.Add(stackPanel2);
		Grid.SetColumn(stackPanel2, 1);
		return new StackPanel
		{
			Spacing = 6.0,
			Children = { (UIElement)grid }
		};
	}

	private StackPanel BuildControlBar()
	{
		_startBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue768", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "开始测试", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(16.0, 8.0, 16.0, 8.0)
		};
		_startBtn.Click += OnStartClick;
		_stopBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue71a", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "停止", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			IsEnabled = false
		};
		_stopBtn.Click += OnStopClick;
		_exportBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\uede1", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "导出 PDF", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			IsEnabled = false
		};
		_exportBtn.Click += OnExportClick;
		_historyBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue81c", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "历史对比", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0)
		};
		_historyBtn.Click += OnHistoryClick;
		_uploadBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue898", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "上传排行", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0)
		};
		_uploadBtn.Click += OnUploadClick;
		_rankingBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue9d5", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "排行榜", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0)
		};
		_rankingBtn.Click += OnRankingClick;
		_globalProgress = new ProgressBar
		{
			Value = 0.0,
			Maximum = 100.0,
			Height = 4.0
		};
		_statusText = new TextBlock
		{
			Text = "",
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
		};
		_chkCpu = new CheckBox { Content = "CPU", IsChecked = true, FontSize = 12.0 };
		_chkGpu = new CheckBox { Content = "GPU", IsChecked = true, FontSize = 12.0 };
		_chkMem = new CheckBox { Content = "内存", IsChecked = true, FontSize = 12.0 };
		_chkDisk = new CheckBox { Content = "硬盘", IsChecked = true, FontSize = 12.0 };
		_chkBrowser = new CheckBox { Content = "浏览器", IsChecked = true, FontSize = 12.0 };
		StackPanel stackPanel = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 12.0
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "测试项目:",
			FontSize = 12.0,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(_chkCpu);
		stackPanel.Children.Add(_chkGpu);
		stackPanel.Children.Add(_chkMem);
		stackPanel.Children.Add(_chkDisk);
		stackPanel.Children.Add(_chkBrowser);
		StackPanel stackPanel2 = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel2.Children.Add(_startBtn);
		stackPanel2.Children.Add(_stopBtn);
		stackPanel2.Children.Add(_exportBtn);
		stackPanel2.Children.Add(_historyBtn);
		stackPanel2.Children.Add(_uploadBtn);
		stackPanel2.Children.Add(_rankingBtn);
		return new StackPanel
		{
			Spacing = 8.0,
			Children =
			{
				(UIElement)stackPanel,
				(UIElement)stackPanel2,
				(UIElement)_globalProgress,
				(UIElement)_statusText
			}
		};
	}

	private async void OnStartClick(object sender, RoutedEventArgs e)
	{
		if (_isRunning) return;
		bool runCpu = _chkCpu.IsChecked == true;
		bool runGpu = _chkGpu.IsChecked == true;
		bool runMem = _chkMem.IsChecked == true;
		bool runDisk = _chkDisk.IsChecked == true;
		bool runBrowser = _chkBrowser.IsChecked == true;
		if (!runCpu && !runGpu && !runMem && !runDisk && !runBrowser)
		{
			_statusText.Text = "请至少选择一个测试项目";
			return;
		}
		_isRunning = true;
		_startBtn.IsEnabled = false;
		_stopBtn.IsEnabled = true;
		_exportBtn.IsEnabled = false;
		SetCheckboxesEnabled(false);
		_cts = new CancellationTokenSource();
		ResetUI();
		try
		{
			var result = new PerformanceBenchmarkResult
			{
				TestTime = DateTime.Now,
				DurationMode = "Deep"
			};
			PerformanceBenchmarkService.PopulateHardwareInfo(result);
			Stopwatch sw = Stopwatch.StartNew();
			var progress = new Progress<BenchmarkProgress>(p =>
			{
				_window.DispatcherQueue.TryEnqueue(() =>
				{
					_statusText.Text = $"{p.Phase} · {p.SubPhase}  {p.Detail}  (可随时点击停止)";
					_globalProgress.Value = p.Progress * 100.0;
				});
			});
			if (runCpu)
			{
				result.Cpu = await Task.Run(() => PerformanceBenchmarkService.RunCpuBenchmark(60, progress, _cts.Token), _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
				_window.DispatcherQueue.TryEnqueue(() => UpdateCpuUI(result));
				string coreToCoreExe = PerformanceBenchmarkService.FindCoreToCoreLatencyExe();
				if (coreToCoreExe != null)
				{
					var (csv, _) = await ShowCoreToCoreLatencyDialog(coreToCoreExe);
					if (!string.IsNullOrEmpty(csv))
					{
						int maxCores = Math.Min(Environment.ProcessorCount, 64);
						var matrix = PerformanceBenchmarkService.ParseCoreToCoreCsv(csv, maxCores);
						PerformanceBenchmarkService.ApplyLatencyResult(result.Cpu, matrix);
						_latencyHeatmapPath = PerformanceBenchmarkService.GenerateLatencyHeatmap(matrix);
						_window.DispatcherQueue.TryEnqueue(() =>
						{
							ShowLatencyHeatmap(_latencyHeatmapPath);
							UpdateScoreRow(_cpuLatencyScoreText, result.Cpu.LatencyScore);
						});
					}
				}
			}
			if (runMem)
			{
				result.Memory = await Task.Run(() => PerformanceBenchmarkService.RunMemoryBenchmark(1, progress, _cts.Token), _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
				_window.DispatcherQueue.TryEnqueue(() => UpdateMemoryUI(result));
			}
			if (runDisk)
			{
				result.Disk = await Task.Run(() => PerformanceBenchmarkService.RunDiskBenchmark(20, progress, _cts.Token), _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
				_window.DispatcherQueue.TryEnqueue(() => UpdateDiskUI(result));
			}
			if (runGpu)
			{
				result.Gpu = await Task.Run(() => PerformanceBenchmarkService.RunGpuBenchmarkFurMark(60, progress, _cts.Token), _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
				_window.DispatcherQueue.TryEnqueue(() => UpdateGpuUI(result));
			}
			if (runBrowser)
			{
				result.Browser = new BrowserBenchmarkResult();
				await RunBrowserTestsAsync(result, 60, _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
			}
			result.GamingScore = PerformanceBenchmarkService.ComputeGamingScore(result);
			result.GamingGrade = PerformanceBenchmarkService.ComputeGrade(result.GamingScore);
			result.OfficeScore = PerformanceBenchmarkService.ComputeOfficeScore(result);
			result.OfficeGrade = PerformanceBenchmarkService.ComputeGrade(result.OfficeScore);
			sw.Stop();
			result.TotalDuration = sw.Elapsed;
			_window.DispatcherQueue.TryEnqueue(() =>
			{
				UpdateTopCard(_gamingScoreText, _gamingGradeText, _gamingBar, result.GamingScore, result.GamingGrade);
				UpdateTopCard(_officeScoreText, _officeGradeText, _officeBar, result.OfficeScore, result.OfficeGrade);
			});
			PerformanceBenchmarkService.SaveHistory(result);
			_result = result;
			_exportBtn.IsEnabled = true;
			_statusText.Text = $"测试完成！总耗时: {result.TotalDuration:mm\\mss\\s}";
			_globalProgress.Value = 100.0;
		}
		catch (OperationCanceledException)
		{
			_statusText.Text = "测试已取消";
		}
		catch (Exception ex)
		{
			_statusText.Text = "测试出错: " + ex.Message;
		}
		finally
		{
			_isRunning = false;
			_startBtn.IsEnabled = true;
			_stopBtn.IsEnabled = false;
			SetCheckboxesEnabled(true);
		}
	}

	private void SetCheckboxesEnabled(bool enabled)
	{
		_chkCpu.IsEnabled = enabled;
		_chkGpu.IsEnabled = enabled;
		_chkMem.IsEnabled = enabled;
		_chkDisk.IsEnabled = enabled;
		_chkBrowser.IsEnabled = enabled;
	}

	private void OnStopClick(object sender, RoutedEventArgs e)
	{
		PerformanceBenchmarkService.Cancel();
		_cts.Cancel();
	}

	private async Task<(string csv, string stderr)> ShowCoreToCoreLatencyDialog(string exePath)
	{
		TextBlock outputText = new()
		{
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap,
			IsTextSelectionEnabled = true,
			Text = "正在运行 core-to-core-latency...\n"
		};
		ScrollViewer scroll = new()
		{
			Content = outputText,
			MaxHeight = 400.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		ContentDialog dialog = new()
		{
			Title = "核间延迟测试 — core-to-core-latency",
			Content = scroll,
			CloseButtonText = "取消",
			XamlRoot = _window.Content.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		var csvBuilder = new StringBuilder();
		var stderrBuilder = new StringBuilder();
		var tcs = new TaskCompletionSource<(string csv, string stderr)>();
		bool procExited = false;
		ProcessStartInfo startInfo = new()
		{
			FileName = exePath,
			Arguments = "--csv",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		Process proc = null;
		try
		{
			proc = Process.Start(startInfo);
			if (proc == null)
			{
				outputText.Text = "无法启动 core-to-core-latency";
				return (csv: "", stderr: "");
			}
			proc.OutputDataReceived += (_, e) =>
			{
				if (e.Data != null) csvBuilder.AppendLine(e.Data);
			};
			proc.ErrorDataReceived += (_, e) =>
			{
				if (e.Data != null)
				{
					stderrBuilder.AppendLine(e.Data);
					string captured = e.Data;
					_window.DispatcherQueue.TryEnqueue(() =>
					{
						TextBlock textBlock = outputText;
						textBlock.Text = textBlock.Text + captured + "\n";
						scroll.ChangeView(null, scroll.ScrollableHeight, null);
					});
				}
			};
			proc.BeginOutputReadLine();
			proc.BeginErrorReadLine();
			proc.EnableRaisingEvents = true;
			proc.Exited += (_, _) =>
			{
				procExited = true;
				_window.DispatcherQueue.TryEnqueue(() =>
				{
					outputText.Text += "\n--- 测试完成 ---\n";
					scroll.ChangeView(null, scroll.ScrollableHeight, null);
					if (!tcs.Task.IsCompleted)
					{
						tcs.SetResult((csvBuilder.ToString(), stderrBuilder.ToString()));
					}
					dialog.Hide();
				});
			};
		}
		catch (Exception ex)
		{
			outputText.Text = "启动失败: " + ex.Message;
			return (csv: "", stderr: "");
		}
		dialog.ShowAsync().AsTask().ContinueWith(_ =>
		{
			if (!procExited && proc != null)
			{
				try { if (!proc.HasExited) proc.Kill(); } catch { }
			}
			_window.DispatcherQueue.TryEnqueue(() =>
			{
				if (!tcs.Task.IsCompleted)
				{
					tcs.SetResult((csvBuilder.ToString(), stderrBuilder.ToString()));
				}
			});
		});
		return await tcs.Task;
	}

	private async Task RunBrowserTestsAsync(PerformanceBenchmarkResult result, int gpuSec, CancellationToken ct)
	{
		WebView2 webView = new() { Width = 900.0, Height = 600.0 };
		TextBlock item = new()
		{
			Text = "正在加载浏览器测试...",
			FontSize = 13.0,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		};
		StackPanel stackPanel = new() { Spacing = 4.0 };
		stackPanel.Children.Add(webView);
		stackPanel.Children.Add(item);
		ContentDialog dialog = new()
		{
			Title = "浏览器性能测试",
			Content = stackPanel,
			CloseButtonText = "取消",
			XamlRoot = _window.Content.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		await webView.EnsureCoreWebView2Async();
		var tcs = new TaskCompletionSource<BrowserBenchmarkResult>();
		webView.CoreWebView2.WebMessageReceived += (_, args) =>
		{
			try
			{
				JsonElement root = JsonDocument.Parse(args.TryGetWebMessageAsString()).RootElement;
				var br = new BrowserBenchmarkResult();
				br.JsScore = root.TryGetProperty("jsScore", out var v1) ? v1.GetInt32() : 0;
				br.JsDetail = root.TryGetProperty("jsDetail", out var v2) ? v2.GetString() ?? "" : "";
				br.DomScore = root.TryGetProperty("domScore", out var v3) ? v3.GetInt32() : 0;
				br.DomDetail = root.TryGetProperty("domDetail", out var v4) ? v4.GetString() ?? "" : "";
				br.CardScore = root.TryGetProperty("cardScore", out var v5) ? v5.GetInt32() : 0;
				br.CardDetail = root.TryGetProperty("cardDetail", out var v6) ? v6.GetString() ?? "" : "";
				br.CssScore = root.TryGetProperty("cssScore", out var v7) ? v7.GetInt32() : 0;
				br.CssDetail = root.TryGetProperty("cssDetail", out var v8) ? v8.GetString() ?? "" : "";
				br.LayoutScore = root.TryGetProperty("layoutScore", out var v9) ? v9.GetInt32() : 0;
				br.LayoutDetail = root.TryGetProperty("layoutDetail", out var v10) ? v10.GetString() ?? "" : "";
				br.EventScore = root.TryGetProperty("eventScore", out var v11) ? v11.GetInt32() : 0;
				br.EventDetail = root.TryGetProperty("eventDetail", out var v12) ? v12.GetString() ?? "" : "";
				br.TotalScore = root.TryGetProperty("totalScore", out var v13) ? v13.GetInt32() : 0;
				br.Grade = PerformanceBenchmarkService.ComputeGrade(br.TotalScore);
				tcs.TrySetResult(br);
			}
			catch { }
		};
		dialog.ShowAsync().AsTask().ContinueWith(_ =>
		{
			_window.DispatcherQueue.TryEnqueue(() =>
			{
				if (!tcs.Task.IsCompleted) tcs.TrySetCanceled();
			});
		});
		string uriString = Path.Combine(AppContext.BaseDirectory, "Assets", "Benchmark", "browser-benchmark.html");
		webView.CoreWebView2.Navigate(new Uri(uriString).AbsoluteUri);
		using (ct.Register(() => tcs.TrySetCanceled()))
		{
			try
			{
				BrowserBenchmarkResult br = await tcs.Task;
				result.Browser = br;
				_window.DispatcherQueue.TryEnqueue(() =>
				{
					UpdateDetailRow(_brJsScoreText, _brJsDetailText, br.JsScore, br.JsDetail);
					UpdateDetailRow(_brDomScoreText, _brDomDetailText, br.DomScore, br.DomDetail);
					UpdateDetailRow(_brCardScoreText, _brCardDetailText, br.CardScore, br.CardDetail);
					UpdateDetailRow(_brCssScoreText, _brCssDetailText, br.CssScore, br.CssDetail);
					UpdateDetailRow(_brLayoutScoreText, _brLayoutDetailText, br.LayoutScore, br.LayoutDetail);
					UpdateDetailRow(_brEventScoreText, _brEventDetailText, br.EventScore, br.EventDetail);
				});
				dialog.Hide();
			}
			catch (OperationCanceledException) { throw; }
			catch { }
		}
	}

	private void UpdateCpuUI(PerformanceBenchmarkResult r)
	{
		UpdateScoreRow(_cpuSingleScoreText, r.Cpu.SingleCoreScore);
		UpdateScoreRow(_cpuMultiScoreText, r.Cpu.MultiCoreScore);
		UpdateScoreRow(_cpuLatencyScoreText, r.Cpu.LatencyScore);
	}

	private void UpdateMemoryUI(PerformanceBenchmarkResult r)
	{
		_memCapacityText.Text = $"{r.Memory.TotalCapacityGB:F0} GB";
	}

	private void UpdateGpuUI(PerformanceBenchmarkResult r)
	{
		UpdateScoreRow(_gpuRenderScoreText, r.Gpu.RenderScore);
		UpdateDetailRow(_gpuFurMarkScoreText, _gpuAvgFpsText, r.Gpu.FurMarkScore, $"平均 {r.Gpu.AvgFps:F0} FPS");
		UpdateDetailRow(_gpuMinFpsText, _gpuMaxFpsText, (int)r.Gpu.MinFps, $"最低 {r.Gpu.MinFps:F0} / 最高 {r.Gpu.MaxFps:F0}");
	}

	private void UpdateDiskUI(PerformanceBenchmarkResult r)
	{
		UpdateDetailRow(_diskSeqReadScoreText, _diskSeqReadDetailText, r.Disk.SeqReadScore, $"{r.Disk.SeqReadMBs:F0} MB/s");
		UpdateDetailRow(_diskSeqWriteScoreText, _diskSeqWriteDetailText, r.Disk.SeqWriteScore, $"{r.Disk.SeqWriteMBs:F0} MB/s");
		UpdateDetailRow(_disk4KReadScoreText, _disk4KReadDetailText, r.Disk.Random4KReadScore, $"{r.Disk.Random4KReadIops / 1000.0:F0}K IOPS");
		UpdateDetailRow(_disk4KWriteScoreText, _disk4KWriteDetailText, r.Disk.Random4KWriteScore, $"{r.Disk.Random4KWriteIops / 1000.0:F0}K IOPS");
		_diskTempText.Text = r.Disk.Temperature > 0f ? $"{r.Disk.Temperature:F0}℃" : "N/A";
	}

	private void UpdateDetailRow(TextBlock scoreText, TextBlock detailText, int score, string detail)
	{
		scoreText.Text = score.ToString();
		scoreText.Foreground = new SolidColorBrush(ScoreColor(score));
		detailText.Text = detail;
	}

	private void UpdateTopCard(TextBlock scoreText, TextBlock gradeText, ProgressBar bar, int score, string grade)
	{
		scoreText.Text = score.ToString();
		scoreText.Foreground = new SolidColorBrush(GradeColor(grade));
		gradeText.Text = grade;
		gradeText.Foreground = new SolidColorBrush(GradeColor(grade));
		bar.Maximum = Math.Max(score, 100);
		bar.Value = score;
	}

	private void UpdateScoreRow(TextBlock scoreText, int score, string detail = "")
	{
		scoreText.Text = score.ToString();
		scoreText.Foreground = new SolidColorBrush(ScoreColor(score));
	}

	private void ShowLatencyHeatmap(string? imagePath)
	{
		if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;
		try
		{
			BitmapImage source = new(new Uri(imagePath));
			_latencyHeatmapImage.Source = source;
			_latencyHeatmapImage.Visibility = Visibility.Visible;
			_latencyGridContainer.Visibility = Visibility.Visible;
		}
		catch { }
	}

	private static Color GradeColor(string grade) => grade switch
	{
		"S" => ColorS,
		"A+" => ColorAPlus,
		"A" => ColorA,
		"B+" => ColorBPlus,
		"B" => ColorB,
		"C" => ColorC,
		_ => ColorD
	};

	private static Color ScoreColor(int score)
	{
		if (score >= 75) return score >= 130 ? ColorS : score >= 100 ? ColorAPlus : ColorA;
		if (score >= 40) return score >= 55 ? ColorBPlus : ColorB;
		return score >= 20 ? ColorC : ColorD;
	}

	private void ResetUI()
	{
		_gamingScoreText.Text = "—";
		_gamingGradeText.Text = "";
		_gamingBar.Value = 0.0;
		_officeScoreText.Text = "—";
		_officeGradeText.Text = "";
		_officeBar.Value = 0.0;
		_cpuSingleScoreText.Text = "—";
		_cpuMultiScoreText.Text = "—";
		_cpuLatencyScoreText.Text = "—";
		_latencyGridContainer.Visibility = Visibility.Collapsed;
		_latencyHeatmapImage.Source = null;
		_latencyHeatmapImage.Visibility = Visibility.Collapsed;
		_latencyHeatmapPath = null;
		_gpuRenderScoreText.Text = "—";
		_gpuFurMarkScoreText.Text = "—";
		_gpuAvgFpsText.Text = "";
		_gpuMinFpsText.Text = "—";
		_gpuMaxFpsText.Text = "";
		_memCapacityText.Text = "—";
		_diskSeqReadScoreText.Text = "—";
		_diskSeqReadDetailText.Text = "";
		_diskSeqWriteScoreText.Text = "—";
		_diskSeqWriteDetailText.Text = "";
		_disk4KReadScoreText.Text = "—";
		_disk4KReadDetailText.Text = "";
		_disk4KWriteScoreText.Text = "—";
		_disk4KWriteDetailText.Text = "";
		_diskTempText.Text = "—";
		_brJsScoreText.Text = "—";
		_brJsDetailText.Text = "";
		_brDomScoreText.Text = "—";
		_brDomDetailText.Text = "";
		_brCardScoreText.Text = "—";
		_brCardDetailText.Text = "";
		_brCssScoreText.Text = "—";
		_brCssDetailText.Text = "";
		_brLayoutScoreText.Text = "—";
		_brLayoutDetailText.Text = "";
		_brEventScoreText.Text = "—";
		_brEventDetailText.Text = "";
		_globalProgress.Value = 0.0;
	}

	private async void OnExportClick(object sender, RoutedEventArgs e)
	{
		if (_result == null) return;
		try
		{
			_statusText.Text = "正在准备报告...";
			Window obj = new() { Title = "性能测试报告" };
			AppWindow appWindow = obj.AppWindow;
			appWindow.Resize(new SizeInt32(900, 900));
			appWindow.Move(new PointInt32(_window.AppWindow.Position.X + 100, _window.AppWindow.Position.Y + 50));
			WebView2 pdfWv = new();
			Button button = new()
			{
				Content = "打印/导出PDF",
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin = new Thickness(0.0, 8.0, 16.0, 8.0)
			};
			TextBlock statusLabel = new()
			{
				Text = "报告加载中...",
				FontSize = 12.0,
				Margin = new Thickness(16.0, 0.0, 0.0, 8.0),
				Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid grid = new() { Height = 44.0 };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.Children.Add(statusLabel);
			Grid.SetColumn(statusLabel, 0);
			grid.Children.Add(button);
			Grid.SetColumn(button, 1);
			Grid grid2 = new()
			{
				RowDefinitions =
				{
					new RowDefinition { Height = GridLength.Auto },
					new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) }
				},
				Children = { (UIElement)grid }
			};
			Grid.SetRow(grid, 0);
			grid2.Children.Add(pdfWv);
			Grid.SetRow(pdfWv, 1);
			obj.Content = grid2;
			button.Click += async (_, _) =>
			{
				statusLabel.Text = "请在打印对话框中选择\"另存为 PDF\"来导出";
				await pdfWv.CoreWebView2.ExecuteScriptAsync("window.print();");
			};
			obj.Activate();
			await pdfWv.EnsureCoreWebView2Async();
			string folderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Benchmark");
			pdfWv.CoreWebView2.SetVirtualHostNameToFolderMapping("bench.local", folderPath, CoreWebView2HostResourceAccessKind.Allow);
			var reportReady = new TaskCompletionSource<bool>();
			pdfWv.CoreWebView2.WebMessageReceived += (_, args) =>
			{
				try
				{
					if (args.TryGetWebMessageAsString().Contains("report_ready"))
						reportReady.TrySetResult(true);
				}
				catch { }
			};
			pdfWv.CoreWebView2.NavigationCompleted += async (_, args) =>
			{
				if (!args.IsSuccess)
				{
					reportReady.TrySetException(new Exception("导航失败"));
				}
				else
				{
					await Task.Delay(300);
					await pdfWv.CoreWebView2.ExecuteScriptAsync("window.REPORT_DATA=" + PerformanceBenchmarkService.BuildReportJson(_result, _latencyHeatmapPath) + ";window.renderReport();");
				}
			};
			pdfWv.CoreWebView2.Navigate("https://bench.local/generate-report.html");
			await Task.WhenAny(reportReady.Task, Task.Delay(15000));
			statusLabel.Text = "报告已就绪，点击右上角按钮导出PDF";
			_statusText.Text = "报告窗口已打开";
		}
		catch (Exception ex)
		{
			_statusText.Text = "导出失败: " + ex.Message;
		}
	}

	private async void OnHistoryClick(object sender, RoutedEventArgs e)
	{
		List<PerformanceBenchmarkResult> list = PerformanceBenchmarkService.LoadHistory();
		if (list.Count == 0)
		{
			await new ContentDialog
			{
				Title = "历史对比",
				Content = new TextBlock { Text = "暂无历史测试记录", Margin = new Thickness(16.0) },
				CloseButtonText = "关闭",
				XamlRoot = _window.Content.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
			return;
		}
		ContentDialog dialog = new()
		{
			Title = "历史对比",
			CloseButtonText = "关闭",
			XamlRoot = _window.Content.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		StackPanel stackPanel = new()
		{
			Spacing = 8.0,
			Padding = new Thickness(8.0)
		};
		Button button = new()
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue74d", FontSize = 12.0 },
					(UIElement)new TextBlock { Text = "清空全部", FontSize = 12.0 }
				}
			},
			FontSize = 12.0,
			Padding = new Thickness(8.0, 4.0, 8.0, 4.0)
		};
		StackPanel historyStack = new() { Spacing = 6.0 };
		BuildCards();
		button.Click += (_, _) =>
		{
			PerformanceBenchmarkService.ClearHistory();
			dialog.Hide();
		};
		stackPanel.Children.Add(button);
		stackPanel.Children.Add(historyStack);
		dialog.Content = new ScrollViewer
		{
			Content = stackPanel,
			MaxHeight = 400.0
		};
		await dialog.ShowAsync();

		void BuildCards()
		{
			historyStack.Children.Clear();
			List<PerformanceBenchmarkResult> all = PerformanceBenchmarkService.LoadHistory();
			List<PerformanceBenchmarkResult> recent = all.TakeLast(10).Reverse().ToList();
			int skipCount = Math.Max(0, all.Count - 10);
			for (int i = 0; i < recent.Count; i++)
			{
				int idx = skipCount + (recent.Count - 1 - i);
				var r = recent[i];
				Grid grid = new() { Padding = new Thickness(12.0, 8.0, 12.0, 8.0) };
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
				StackPanel stackPanel2 = new() { Spacing = 4.0 };
				stackPanel2.Children.Add(new TextBlock
				{
					Text = $"{r.TestTime:yyyy-MM-dd HH:mm}  ({r.DurationMode})",
					FontSize = 12.0,
					FontWeight = FontWeights.SemiBold
				});
				stackPanel2.Children.Add(new TextBlock
				{
					Text = $"游戏: {r.GamingScore} ({r.GamingGrade})  |  办公: {r.OfficeScore} ({r.OfficeGrade})",
					FontSize = 11.0,
					Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
				});
				Grid.SetColumn(stackPanel2, 0);
				grid.Children.Add(stackPanel2);
				Button button2 = new()
				{
					Content = new FontIcon { Glyph = "\ue74d", FontSize = 11.0 },
					Tag = idx,
					Padding = new Thickness(4.0, 2.0, 4.0, 2.0),
					MinWidth = 0.0,
					MinHeight = 0.0
				};
				button2.Click += async (s, _) =>
				{
					PerformanceBenchmarkService.DeleteHistory((int)((Button)s).Tag);
					BuildCards();
				};
				Grid.SetColumn(button2, 1);
				grid.Children.Add(button2);
				Border item = new()
				{
					Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
					CornerRadius = new CornerRadius(6.0),
					Child = grid
				};
				historyStack.Children.Add(item);
			}
		}
	}

	private async void OnUploadClick(object sender, RoutedEventArgs e)
	{
		if (_result == null)
		{
			await new ContentDialog
			{
				Title = "无测试报告",
				Content = "请先运行一次性能测试，再上传报告。",
				CloseButtonText = "确定",
				XamlRoot = _window.Content.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
			return;
		}
		if (!GitHubAuthService.IsLoggedIn)
		{
			try
			{
				await GitHubAuthService.EnsureAuthenticatedAsync(_window.Content.XamlRoot, CancellationToken.None);
			}
			catch
			{
				await new ContentDialog
				{
					Title = "需要登录",
					Content = "上传报告需要 GitHub 账号，请先在设置中登录。",
					CloseButtonText = "确定",
					XamlRoot = _window.Content.XamlRoot,
					RequestedTheme = ThemeService.CurrentElementTheme
				}.ShowAsync();
				return;
			}
		}
		if (await new ContentDialog
		{
			Title = "上传测试报告",
			Content = $"将上传当前测试报告：\n\nCPU: {_result.CpuName}\nGPU: {_result.GpuName}\n游戏: {_result.GamingScore} ({_result.GamingGrade})\n办公: {_result.OfficeScore} ({_result.OfficeGrade})\n\n报告将通过 PR 提交到社区仓库，合并后出现在排行榜。",
			PrimaryButtonText = "上传",
			CloseButtonText = "取消",
			XamlRoot = _window.Content.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		}.ShowAsync() != ContentDialogResult.Primary)
		{
			return;
		}
		ContentDialog progressDlg = new()
		{
			Title = "正在上传",
			Content = new ProgressBar { IsIndeterminate = true },
			XamlRoot = _window.Content.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		progressDlg.ShowAsync();
		try
		{
			var progress = new Progress<string>(msg =>
			{
				_window.DispatcherQueue.TryEnqueue(() =>
				{
					progressDlg.Content = new StackPanel
					{
						Spacing = 8.0,
						Children =
						{
							(UIElement)new TextBlock { Text = msg },
							(UIElement)new ProgressBar { IsIndeterminate = true }
						}
					};
				});
			});
			string prUrl = await BenchmarkCloudService.UploadReportAsync(_result, progress, CancellationToken.None);
			progressDlg.Hide();
			if (await new ContentDialog
			{
				Title = "上传成功",
				Content = "报告已通过 PR 提交，合并后将出现在排行榜。\n\nPR 链接：" + prUrl,
				PrimaryButtonText = "打开 PR",
				CloseButtonText = "关闭",
				XamlRoot = _window.Content.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync() == ContentDialogResult.Primary)
			{
				await Launcher.LaunchUriAsync(new Uri(prUrl));
			}
		}
		catch (Exception ex)
		{
			progressDlg.Hide();
			await new ContentDialog
			{
				Title = "上传失败",
				Content = ex.Message,
				CloseButtonText = "确定",
				XamlRoot = _window.Content.XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
		}
	}

	private void OnRankingClick(object sender, RoutedEventArgs e)
	{
		var tool = new BenchmarkCloudTool();
		var context = new BuiltinToolContext { XamlRoot = _window.Content.XamlRoot };
		tool.ExecuteAsync(context);
	}
}
