using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using TubaWinUi3.Services.ViVe;
using WinRT.Interop;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class ViVeFeatureWindow : Window
{
	private static readonly Color AccentGreen = Color.FromArgb(byte.MaxValue, 74, 222, 128);

	private static readonly Color AccentRed = Color.FromArgb(byte.MaxValue, 248, 113, 113);

	private static readonly Color AccentBlue = Color.FromArgb(byte.MaxValue, 96, 165, 250);

	private List<ViVeFeatureEntry>? _allFeatures;

	private string _searchFilter = "";

	private string _stateFilter = "全部状态";

	private SolidColorBrush AccentGreenBrush { get; } = new SolidColorBrush(AccentGreen);

	private SolidColorBrush AccentRedBrush { get; } = new SolidColorBrush(AccentRed);

	public ViVeFeatureWindow()
	{
		InitializeComponent();
		base.AppWindow.Title = "Windows 功能开关 (ViVeGUI)";
		base.AppWindow.Resize(new SizeInt32(960, 720));
		base.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
		if (base.AppWindow.Presenter is OverlappedPresenter overlappedPresenter)
		{
			overlappedPresenter.IsResizable = true;
			overlappedPresenter.IsMaximizable = true;
		}
		if (base.Content is FrameworkElement frameworkElement)
		{
			frameworkElement.RequestedTheme = ThemeService.CurrentElementTheme;
		}
		ApplyThemeColors();
		if (!ViVeService.IsSupported)
		{
			UnsupportedBar.IsOpen = true;
		}
		else
		{
			LoadDataAsync();
		}
	}

	private void ApplyThemeColors()
	{
		bool flag = ThemeService.CurrentTheme == AppTheme.Dark || (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
		Color color = (flag ? Color.FromArgb(byte.MaxValue, 44, 44, 44) : Color.FromArgb(byte.MaxValue, 249, 249, 249));
		Color color2 = (flag ? Color.FromArgb(byte.MaxValue, 59, 59, 59) : Color.FromArgb(byte.MaxValue, 228, 228, 228));
		Color color3 = (flag ? Color.FromArgb(byte.MaxValue, 38, 38, 38) : Color.FromArgb(byte.MaxValue, 244, 244, 244));
		HeaderBorder.Background = new SolidColorBrush(color3);
		ListBorder.BorderBrush = new SolidColorBrush(color2);
		Border[] array = new Border[4] { StatTotal, StatEnabled, StatDisabled, StatDefault };
		foreach (Border obj in array)
		{
			obj.Background = new SolidColorBrush(color);
			obj.BorderBrush = new SolidColorBrush(color2);
		}
		AppWindowTitleBar titleBar = base.AppWindow.TitleBar;
		if (flag)
		{
			titleBar.ButtonForegroundColor = Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
			titleBar.ButtonBackgroundColor = Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue);
			titleBar.ButtonHoverForegroundColor = Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
			titleBar.ButtonHoverBackgroundColor = Color.FromArgb(byte.MaxValue, 50, 50, 50);
			titleBar.ButtonPressedForegroundColor = Color.FromArgb(byte.MaxValue, 180, 180, 180);
			titleBar.ButtonPressedBackgroundColor = Color.FromArgb(byte.MaxValue, 30, 30, 30);
			titleBar.BackgroundColor = Color.FromArgb(byte.MaxValue, 32, 32, 32);
			titleBar.InactiveBackgroundColor = Color.FromArgb(byte.MaxValue, 32, 32, 32);
		}
		else
		{
			titleBar.ButtonForegroundColor = Color.FromArgb(byte.MaxValue, 30, 30, 30);
			titleBar.ButtonBackgroundColor = Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue);
			titleBar.ButtonHoverForegroundColor = Color.FromArgb(byte.MaxValue, 30, 30, 30);
			titleBar.ButtonHoverBackgroundColor = Color.FromArgb(byte.MaxValue, 230, 230, 230);
			titleBar.ButtonPressedForegroundColor = Color.FromArgb(byte.MaxValue, 100, 100, 100);
			titleBar.ButtonPressedBackgroundColor = Color.FromArgb(byte.MaxValue, 210, 210, 210);
			titleBar.BackgroundColor = Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue);
			titleBar.InactiveBackgroundColor = Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue);
		}
		titleBar.ButtonInactiveForegroundColor = Color.FromArgb(byte.MaxValue, 160, 160, 160);
	}

	private async Task LoadDataAsync()
	{
		try
		{
			LoadingRing.IsActive = true;
			LoadingPanel.Visibility = Visibility.Visible;
			ListBorder.Visibility = Visibility.Collapsed;
			HeaderBorder.Visibility = Visibility.Collapsed;
			EmptyPanel.Visibility = Visibility.Collapsed;
		}
		catch
		{
			return;
		}
		List<ViVeFeatureEntry> features = null;
		try
		{
			features = await Task.Run(() => ViVeService.QueryFeatures(ViVeStoreType.Runtime));
		}
		catch
		{
		}
		_allFeatures = features;
		try
		{
			base.DispatcherQueue.TryEnqueue(delegate
			{
				UpdateStats();
				ApplyFilter();
				LoadingRing.IsActive = false;
				LoadingPanel.Visibility = Visibility.Collapsed;
			});
		}
		catch
		{
		}
	}

	private void UpdateStats()
	{
		if (_allFeatures != null)
		{
			TotalCountText.Text = _allFeatures.Count.ToString();
			EnabledCountText.Text = _allFeatures.Count((ViVeFeatureEntry f) => f.EnabledState == RTL_FEATURE_ENABLED_STATE.Enabled).ToString();
			DisabledCountText.Text = _allFeatures.Count((ViVeFeatureEntry f) => f.EnabledState == RTL_FEATURE_ENABLED_STATE.Disabled).ToString();
			DefaultCountText.Text = _allFeatures.Count((ViVeFeatureEntry f) => f.EnabledState == RTL_FEATURE_ENABLED_STATE.Default).ToString();
		}
	}

	private void ApplyFilter()
	{
		if (_allFeatures == null)
		{
			return;
		}
		IEnumerable<ViVeFeatureEntry> enumerable = _allFeatures.AsEnumerable();
		if (_stateFilter != "全部状态")
		{
			enumerable = _stateFilter switch
			{
				"已开启" => enumerable.Where((ViVeFeatureEntry viVeFeatureEntry) => viVeFeatureEntry.EnabledState == RTL_FEATURE_ENABLED_STATE.Enabled), 
				"已关闭" => enumerable.Where((ViVeFeatureEntry viVeFeatureEntry) => viVeFeatureEntry.EnabledState == RTL_FEATURE_ENABLED_STATE.Disabled), 
				"默认(未修改)" => enumerable.Where((ViVeFeatureEntry viVeFeatureEntry) => viVeFeatureEntry.EnabledState == RTL_FEATURE_ENABLED_STATE.Default), 
				_ => enumerable, 
			};
		}
		if (!string.IsNullOrWhiteSpace(_searchFilter))
		{
			string f = _searchFilter.Trim();
			enumerable = enumerable.Where((ViVeFeatureEntry e) => e.FeatureId.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) || (e.Name != null && e.Name.Contains(f, StringComparison.OrdinalIgnoreCase)));
		}
		List<ViVeFeatureEntry> list = enumerable.ToList();
		CountText.Text = $"{list.Count} 项";
		ListContainer.Children.Clear();
		if (list.Count == 0)
		{
			ListBorder.Visibility = Visibility.Collapsed;
			HeaderBorder.Visibility = Visibility.Collapsed;
			EmptyPanel.Visibility = Visibility.Visible;
			return;
		}
		ListBorder.Visibility = Visibility.Visible;
		HeaderBorder.Visibility = Visibility.Visible;
		EmptyPanel.Visibility = Visibility.Collapsed;
		foreach (ViVeFeatureEntry item in list)
		{
			ListContainer.Children.Add(CreateFeatureRow(item));
		}
	}

	private Border CreateFeatureRow(ViVeFeatureEntry entry)
	{
		Color color;
		Color color2;
		string text;
		switch (entry.EnabledState)
		{
		case RTL_FEATURE_ENABLED_STATE.Enabled:
			color = Color.FromArgb(40, 74, 222, 128);
			color2 = AccentGreen;
			text = "已开启";
			break;
		case RTL_FEATURE_ENABLED_STATE.Disabled:
			color = Color.FromArgb(40, 248, 113, 113);
			color2 = AccentRed;
			text = "已关闭";
			break;
		default:
			color = Color.FromArgb(40, 160, 160, 160);
			color2 = Color.FromArgb(byte.MaxValue, 160, 160, 160);
			text = "默认";
			break;
		}
		Border border = new Border
		{
			Padding = new Thickness(8.0, 2.0, 8.0, 2.0),
			CornerRadius = new CornerRadius(4.0),
			Background = new SolidColorBrush(color),
			Child = new TextBlock
			{
				Text = text,
				FontSize = 11.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(color2)
			}
		};
		TextBlock textBlock = new TextBlock
		{
			Text = entry.FeatureId.ToString(),
			FontSize = 13.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = new SolidColorBrush(AccentBlue),
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock textBlock2 = new TextBlock
		{
			Text = (entry.Name ?? "(未知)"),
			FontSize = 13.0,
			Foreground = new SolidColorBrush((entry.Name != null) ? ThemeColors.PrimaryText : ThemeColors.DimText),
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Button button = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4.0,
				Children = 
				{
					(UIElement)new FontIcon
					{
						Glyph = "\ue73e",
						FontSize = 11.0
					},
					(UIElement)new TextBlock
					{
						Text = "开启",
						FontSize = 12.0
					}
				}
			},
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			Tag = entry
		};
		button.Click += EnableBtn_Click;
		Button button2 = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4.0,
				Children = 
				{
					(UIElement)new FontIcon
					{
						Glyph = "\ue894",
						FontSize = 11.0
					},
					(UIElement)new TextBlock
					{
						Text = "关闭",
						FontSize = 12.0
					}
				}
			},
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			Tag = entry
		};
		button2.Click += DisableBtn_Click;
		Button button3 = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4.0,
				Children = 
				{
					(UIElement)new FontIcon
					{
						Glyph = "\ue72c",
						FontSize = 11.0
					},
					(UIElement)new TextBlock
					{
						Text = "还原",
						FontSize = 12.0
					}
				}
			},
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			Tag = entry
		};
		button3.Click += ResetBtn_Click;
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6.0
		};
		stackPanel.Children.Add(button);
		stackPanel.Children.Add(button2);
		stackPanel.Children.Add(button3);
		Grid grid = new Grid
		{
			ColumnSpacing = 10.0
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(80.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(80.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(220.0)
		});
		grid.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		grid.Children.Add(border);
		Grid.SetColumn(border, 1);
		grid.Children.Add(textBlock2);
		Grid.SetColumn(textBlock2, 2);
		grid.Children.Add(stackPanel);
		Grid.SetColumn(stackPanel, 3);
		Color color3 = ((ThemeService.CurrentTheme == AppTheme.Dark || (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark)) ? Color.FromArgb(byte.MaxValue, 59, 59, 59) : Color.FromArgb(byte.MaxValue, 228, 228, 228));
		return new Border
		{
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			BorderBrush = new SolidColorBrush(color3),
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
			Child = grid
		};
	}

	private async void EnableBtn_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is Button { Tag: var tag }))
		{
			return;
		}
		ViVeFeatureEntry entry = tag as ViVeFeatureEntry;
		if (entry != null)
		{
			await ExecuteFeatureAction(() => ViVeService.EnableFeature(entry.FeatureId, entry.Store), "开启");
		}
	}

	private async void DisableBtn_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is Button { Tag: var tag }))
		{
			return;
		}
		ViVeFeatureEntry entry = tag as ViVeFeatureEntry;
		if (entry != null)
		{
			await ExecuteFeatureAction(() => ViVeService.DisableFeature(entry.FeatureId, entry.Store), "关闭");
		}
	}

	private async void ResetBtn_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is Button { Tag: var tag }))
		{
			return;
		}
		ViVeFeatureEntry entry = tag as ViVeFeatureEntry;
		if (entry != null)
		{
			await ExecuteFeatureAction(() => ViVeService.ResetFeature(entry.FeatureId, entry.Store), "还原");
		}
	}

	private async Task ExecuteFeatureAction(Func<ViVeResult> action, string actionLabel)
	{
		ViVeResult result = await Task.Run(action);
		base.DispatcherQueue.TryEnqueue(delegate
		{
			if (result.Success)
			{
				ShowResult(InfoBarSeverity.Success, actionLabel + "成功", "功能状态已更新，部分功能可能需要重启后生效");
			}
			else
			{
				ShowResult(InfoBarSeverity.Error, actionLabel + "失败", result.ErrorMessage ?? "未知错误");
			}
			LoadDataAsync();
		});
	}

	private async void QuickEnableBtn_Click(object sender, RoutedEventArgs e)
	{
		await QuickAction(RTL_FEATURE_ENABLED_STATE.Enabled);
	}

	private async void QuickDisableBtn_Click(object sender, RoutedEventArgs e)
	{
		await QuickAction(RTL_FEATURE_ENABLED_STATE.Disabled);
	}

	private async Task QuickAction(RTL_FEATURE_ENABLED_STATE state)
	{
		string input = FeatureIdBox.Text.Trim();
		if (string.IsNullOrEmpty(input))
		{
			ShowResult(InfoBarSeverity.Warning, "请输入功能 ID", "在输入框中填写要操作的功能 ID 或英文名称");
			return;
		}
		if (!uint.TryParse(input, out var id))
		{
			List<uint> list = await Task.Run(() => ViVeService.SearchFeatureIdsByName(input));
			if (list == null || list.Count == 0)
			{
				ShowResult(InfoBarSeverity.Error, "未找到", "未找到匹配的功能，请检查输入");
				return;
			}
			id = list[0];
		}
		string actionLabel = ((state == RTL_FEATURE_ENABLED_STATE.Enabled) ? "开启" : "关闭");
		ViVeResult result;
		if (state == RTL_FEATURE_ENABLED_STATE.Enabled)
		{
			result = await Task.Run(() => ViVeService.EnableFeature(id, ViVeStoreType.Runtime));
		}
		else
		{
			result = await Task.Run(() => ViVeService.DisableFeature(id, ViVeStoreType.Runtime));
		}
		base.DispatcherQueue.TryEnqueue(delegate
		{
			if (result.Success)
			{
				ShowResult(InfoBarSeverity.Success, actionLabel + "成功", $"功能 {id} 已{actionLabel}，可能需要重启生效");
			}
			else
			{
				ShowResult(InfoBarSeverity.Error, actionLabel + "失败", result.ErrorMessage ?? "未知错误");
			}
			LoadDataAsync();
		});
	}

	private void MoreActionsBtn_Click(object sender, RoutedEventArgs e)
	{
		MenuFlyout menuFlyout = new MenuFlyout();
		MenuFlyoutItem menuFlyoutItem = new MenuFlyoutItem
		{
			Text = "导出当前配置..."
		};
		menuFlyoutItem.Click += ExportItem_Click;
		menuFlyout.Items.Add(menuFlyoutItem);
		MenuFlyoutItem menuFlyoutItem2 = new MenuFlyoutItem
		{
			Text = "导入配置..."
		};
		menuFlyoutItem2.Click += ImportItem_Click;
		menuFlyout.Items.Add(menuFlyoutItem2);
		menuFlyout.Items.Add(new MenuFlyoutSeparator());
		MenuFlyoutItem menuFlyoutItem3 = new MenuFlyoutItem
		{
			Text = "还原所有功能为默认"
		};
		menuFlyoutItem3.Click += FullResetItem_Click;
		menuFlyout.Items.Add(menuFlyoutItem3);
		MenuFlyoutItem menuFlyoutItem4 = new MenuFlyoutItem
		{
			Text = "修复 LKG 存储"
		};
		menuFlyoutItem4.Click += FixLkgItem_Click;
		menuFlyout.Items.Add(menuFlyoutItem4);
		menuFlyout.ShowAt(sender as Button);
	}

	private async void ExportItem_Click(object sender, RoutedEventArgs e)
	{
		FileSavePicker obj = new FileSavePicker
		{
			SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
			FileTypeChoices = { 
			{
				"ViVe 配置文件",
				(IList<string>)new List<string>(1) { ".vive" }
			} },
			SuggestedFileName = $"FeatureConfig_{DateTime.Now:yyyyMMdd_HHmmss}"
		};
		nint windowHandle = WindowNative.GetWindowHandle(this);
		InitializeWithWindow.Initialize(obj, windowHandle);
		StorageFile file = await obj.PickSaveFileAsync();
		if (file == null)
		{
			return;
		}
		ViVeResult result = await Task.Run(() => ViVeService.ExportAllFeatures(file.Path));
		base.DispatcherQueue.TryEnqueue(delegate
		{
			if (result.Success)
			{
				ShowResult(InfoBarSeverity.Success, "导出成功", "配置已导出到 " + file.Path);
			}
			else
			{
				ShowResult(InfoBarSeverity.Error, "导出失败", result.ErrorMessage ?? "未知错误");
			}
		});
	}

	private async void ImportItem_Click(object sender, RoutedEventArgs e)
	{
		FileOpenPicker obj = new FileOpenPicker
		{
			SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
			FileTypeFilter = { ".vive" }
		};
		nint windowHandle = WindowNative.GetWindowHandle(this);
		InitializeWithWindow.Initialize(obj, windowHandle);
		StorageFile file = await obj.PickSingleFileAsync();
		if (file == null)
		{
			return;
		}
		ContentDialogResult contentDialogResult = await new ContentDialog
		{
			Title = "导入配置",
			Content = "是否在导入前清除现有配置？选择「替换」将先还原所有功能为默认，再导入新配置。",
			PrimaryButtonText = "替换导入",
			SecondaryButtonText = "追加导入",
			CloseButtonText = "取消",
			XamlRoot = base.Content.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		}.ShowAsync();
		if (contentDialogResult == ContentDialogResult.None)
		{
			return;
		}
		bool replace = contentDialogResult == ContentDialogResult.Primary;
		ViVeImportResult result = await Task.Run(() => ViVeService.ImportFeatures(file.Path, replace));
		base.DispatcherQueue.TryEnqueue(delegate
		{
			if (result.Success)
			{
				ShowResult(InfoBarSeverity.Success, "导入成功", "已导入配置，建议重启生效");
			}
			else
			{
				ShowResult(InfoBarSeverity.Error, "导入失败", result.ErrorMessage ?? "未知错误");
			}
			LoadDataAsync();
		});
	}

	private async void FullResetItem_Click(object sender, RoutedEventArgs e)
	{
		if (await new ContentDialog
		{
			Title = "还原所有功能",
			Content = "此操作将所有已修改的功能恢复为默认状态，确定继续？",
			PrimaryButtonText = "确定还原",
			CloseButtonText = "取消",
			XamlRoot = base.Content.XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		}.ShowAsync() != ContentDialogResult.Primary)
		{
			return;
		}
		ViVeResult rtResult = await Task.Run(() => ViVeService.FullReset(ViVeStoreType.Runtime));
		ViVeResult bootResult = await Task.Run(() => ViVeService.FullReset(ViVeStoreType.Boot));
		base.DispatcherQueue.TryEnqueue(delegate
		{
			if (rtResult.Success && bootResult.Success)
			{
				ShowResult(InfoBarSeverity.Success, "还原成功", "所有功能已恢复默认，建议重启电脑");
			}
			else
			{
				ShowResult(InfoBarSeverity.Error, "还原失败", rtResult.Success ? bootResult.ErrorMessage : (rtResult.ErrorMessage ?? "未知错误"));
			}
			LoadDataAsync();
		});
	}

	private async void FixLkgItem_Click(object sender, RoutedEventArgs e)
	{
		ViVeResult result = await Task.Run(() => ViVeService.FixLKG());
		base.DispatcherQueue.TryEnqueue(delegate
		{
			if (result.Success)
			{
				ShowResult(InfoBarSeverity.Success, "修复成功", "LKG 存储已修复");
			}
			else
			{
				ShowResult(InfoBarSeverity.Error, "修复失败", result.ErrorMessage ?? "未知错误");
			}
		});
	}

	private void ShowResult(InfoBarSeverity severity, string title, string message)
	{
		ResultBar.Severity = severity;
		ResultBar.Title = title;
		ResultBar.Message = message;
		ResultBar.IsOpen = true;
	}

	private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
	{
		_searchFilter = sender.Text;
		ApplyFilter();
	}

	private void StateFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		_stateFilter = (StateFilterCombo.SelectedItem as string) ?? "全部状态";
		ApplyFilter();
	}

	private void RefreshBtn_Click(object sender, RoutedEventArgs e)
	{
		if (ViVeService.IsSupported)
		{
			LoadDataAsync();
		}
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
