using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class WindowsImageWindow : Window
{
    private List<WindowsImageEntry>? _allEntries;
    private string _filter = "";
    private string _categoryFilter = "全部";
    private string _langFilter = "全部语言";
    private WindowsImageEntry? _msResolvedEntry;

    public WindowsImageWindow()
    {
        InitializeComponent();

        AppWindow.Title = "Windows 镜像下载";
        AppWindow.Resize(new SizeInt32(1060, 720));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        HeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
        ListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);

        LoadMsEditions();
        _ = LoadDataAsync();
    }

    private void LoadMsEditions()
    {
        var editions = MicrosoftOfficialService.GetAvailableEditions();
        MsEditionCombo.ItemsSource = editions;
        MsEditionCombo.DisplayMemberPath = "Name";
    }

    private async Task LoadDataAsync()
    {
        LoadingRing.IsActive = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ListBorder.Visibility = Visibility.Collapsed;
        HeaderBorder.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;

        try
        {
            _allEntries = await WindowsImageService.LoadAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusInfoBar.Title = "加载失败";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        if (_allEntries is null) return;

        var filtered = _allEntries.AsEnumerable();

        if (_categoryFilter != "全部")
            filtered = filtered.Where(e => e.Category == _categoryFilter);

        if (_langFilter != "全部语言")
            filtered = filtered.Where(e => e.Language == _langFilter);

        if (!string.IsNullOrWhiteSpace(_filter))
        {
            var f = _filter.Trim();
            filtered = filtered.Where(e =>
                e.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.FileName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Language.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Arch.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                (e.Updated ?? "").Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        RenderList(list);

        EmptyPanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListBorder.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeaderBorder.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderList(List<WindowsImageEntry> entries)
    {
        ListContainer.Children.Clear();
        foreach (var entry in entries)
            ListContainer.Children.Add(CreateRow(entry));
    }

    private Border CreateRow(WindowsImageEntry entry)
    {
        var nameText = new TextBlock
        {
            Text = entry.DisplayName,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420
        };

        var fileNameText = new TextBlock
        {
            Text = entry.FileName,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var sizeText = new TextBlock
        {
            Text = entry.SizeDisplay,
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var langBadge = MakeBadge(entry.Language, entry.Language == "简体中文"
            ? ThemeColors.AccentBlue
            : entry.Language == "English"
                ? ThemeColors.AccentGreen
                : ThemeColors.AccentPurple);

        var archBadge = MakeBadge(entry.Arch, ThemeColors.AccentOrange);

        var downloadBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE896", FontSize = 11 },
                    new TextBlock { Text = "下载", FontSize = 12 }
                }
            },
            Padding = new Thickness(10, 4, 10, 4),
            Tag = entry
        };
        downloadBtn.Click += DownloadBtn_Click;

        var convertBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE898", FontSize = 11 },
                    new TextBlock { Text = "下载并转ISO", FontSize = 12 }
                }
            },
            Padding = new Thickness(10, 4, 10, 4),
            Tag = entry,
            Visibility = entry.IsEsd ? Visibility.Visible : Visibility.Collapsed
        };
        convertBtn.Click += DownloadAndConvertBtn_Click;

        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actionPanel.Children.Add(downloadBtn);
        actionPanel.Children.Add(convertBtn);

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(nameText); Grid.SetColumn(nameText, 0);
        grid.Children.Add(fileNameText); Grid.SetColumn(fileNameText, 1);
        grid.Children.Add(sizeText); Grid.SetColumn(sizeText, 2);
        grid.Children.Add(langBadge); Grid.SetColumn(langBadge, 3);
        grid.Children.Add(archBadge); Grid.SetColumn(archBadge, 4);
        grid.Children.Add(actionPanel); Grid.SetColumn(actionPanel, 5);

        var tip = new ToolTip { Content = $"{entry.DisplayName}\n{entry.FileName}\n大小: {entry.SizeDisplay}" };
        if (entry.Sha256 is not null) tip.Content += $"\nSHA256: {entry.Sha256[..16]}...";
        if (entry.Updated is not null) tip.Content += $"\n更新: {entry.Updated}";
        ToolTipService.SetToolTip(grid, tip);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static Border MakeBadge(string text, Color color)
    {
        return new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WindowsImageEntry entry } btn) return;
        EnqueueDownload(entry, btn);
    }

    private void EnqueueDownload(WindowsImageEntry entry, FrameworkElement? target = null)
    {
        var destDir = WindowsImageService.GetDownloadDir();
        DownloadQueueService.Enqueue(
            entry.DisplayName,
            entry.DownloadUrl,
            destDir,
            postProcessor: null,
            description: $"{entry.Language} | {entry.Arch} | {entry.SizeDisplay}",
            glyph: "\uE896");

        StatusInfoBar.Title = "已加入下载队列";
        StatusInfoBar.Message = $"{entry.DisplayName} 正在下载至 {destDir}";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;

        ShowQueueTip(entry.DisplayName, target);
    }

    private void ShowQueueTip(string name, FrameworkElement? target)
    {
        QueueTeachingTip.Title = "已加入下载队列";
        QueueTeachingTip.Subtitle = $"{name}\n点击主页搜索框旁的下载按钮可查看进度";
        QueueTeachingTip.IconSource = new SymbolIconSource { Symbol = Symbol.Download };
        QueueTeachingTip.Target = target;
        QueueTeachingTip.IsOpen = true;
    }

    private async void DownloadAndConvertBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WindowsImageEntry entry } btn) return;

        if (!WindowsImageService.IsUltraIsoAvailable)
        {
            var dialog = new ContentDialog
            {
                Title = "需要 UltraISO",
                Content = "ESD 转 ISO 需要安装 UltraISO。\n\n是否前往下载页面？",
                PrimaryButtonText = "前往下载",
                CloseButtonText = "取消",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try { Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.ezbsystems.com/ultraiso/")); } catch { }
            }
            return;
        }

        var destDir = WindowsImageService.GetDownloadDir();
        var isoFileName = Path.ChangeExtension(entry.FileName, ".iso");

        var postProcessor = new DelegatePostProcessor("ESD 转 ISO", async (file, dest, progress, ct) =>
        {
            progress?.Report("正在等待下载完成...");
            var esdFile = Path.Combine(dest, Path.GetFileName(file));
            if (!File.Exists(esdFile)) esdFile = file;

            var isoPath = Path.Combine(dest, isoFileName);

            await WindowsImageService.ConvertEsdToIsoAsync(esdFile, isoPath, progress, ct);

            if (File.Exists(esdFile) && File.Exists(isoPath))
            {
                try { File.Delete(esdFile); } catch { }
            }
        });

        DownloadQueueService.Enqueue(
            entry.DisplayName + " (ESD→ISO)",
            entry.DownloadUrl,
            destDir,
            postProcessor,
            description: $"{entry.Language} | {entry.Arch} | {entry.SizeDisplay} → ISO",
            glyph: "\uE898");

        StatusInfoBar.Title = "已加入下载队列";
        StatusInfoBar.Message = $"{entry.DisplayName} 下载完成后将自动转换为 ISO";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;

        ShowQueueTip(entry.DisplayName + " (ESD→ISO)", btn);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = sender.Text;
        ApplyFilter();
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo.SelectedItem is string s)
            _categoryFilter = s;
        ApplyFilter();
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LangCombo.SelectedItem is string s)
            _langFilter = s;
        ApplyFilter();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CancelConvertBtn_Click(object sender, RoutedEventArgs e)
    {
    }

    private void SourcePivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async void MsEditionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MsEditionCombo.SelectedItem is not MicrosoftEdition edition) return;

        MsLangCombo.IsEnabled = false;
        MsLangCombo.ItemsSource = null;
        MsFetchBtn.IsEnabled = false;
        MsResultPanel.Visibility = Visibility.Collapsed;

        MsProgressRing.Visibility = Visibility.Visible;
        MsStatusText.Visibility = Visibility.Visible;
        MsStatusText.Text = "正在初始化会话...";

        try
        {
            var sessionId = await MicrosoftOfficialService.InitSessionAsync();

            MsStatusText.Text = "正在获取语言列表...";

            var skuId = edition.SkuIds[0];
            var languages = await MicrosoftOfficialService.GetLanguagesAsync(skuId, sessionId);

            if (edition.SkuIds.Length > 1)
            {
                var sessionId2 = await MicrosoftOfficialService.InitSessionAsync();
                var languages2 = await MicrosoftOfficialService.GetLanguagesAsync(edition.SkuIds[1], sessionId2);
                foreach (var lang in languages2)
                {
                    if (!languages.Any(l => l.Name == lang.Name))
                        languages.Add(lang);
                }
            }

            MsLangCombo.ItemsSource = languages;
            MsLangCombo.DisplayMemberPath = "Name";
            MsLangCombo.IsEnabled = languages.Count > 0;
            MsFetchBtn.IsEnabled = languages.Count > 0;

            MsStatusText.Text = $"已获取 {languages.Count} 种语言";
        }
        catch (Exception ex)
        {
            MsStatusText.Text = $"获取语言列表失败: {ex.Message}";
        }
        finally
        {
            MsProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private async void MsFetchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MsEditionCombo.SelectedItem is not MicrosoftEdition edition) return;
        if (MsLangCombo.SelectedItem is not MicrosoftLanguage language) return;

        MsFetchBtn.IsEnabled = false;
        MsResultPanel.Visibility = Visibility.Collapsed;
        MsProgressRing.Visibility = Visibility.Visible;
        MsStatusText.Visibility = Visibility.Visible;
        MsStatusText.Text = "正在获取下载链接...";

        try
        {
            _msResolvedEntry = await MicrosoftOfficialService.ResolveDownloadEntryAsync(edition, language);

            if (_msResolvedEntry is not null)
            {
                MsResultTitle.Text = _msResolvedEntry.DisplayName;
                MsResultInfo.Text = $"架构: {_msResolvedEntry.Arch} | 文件: {_msResolvedEntry.FileName}";
                MsResultPanel.Visibility = Visibility.Visible;
                MsStatusText.Text = "下载链接获取成功（24 小时内有效）";
            }
            else
            {
                MsStatusText.Text = "未能获取下载链接，请稍后重试";
            }
        }
        catch (Exception ex)
        {
            MsStatusText.Text = $"获取失败: {ex.Message}";
        }
        finally
        {
            MsProgressRing.Visibility = Visibility.Collapsed;
            MsFetchBtn.IsEnabled = true;
        }
    }

    private void MsDownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        EnqueueDownload(_msResolvedEntry, sender as FrameworkElement);
    }

    private async void MsOpenBrowserBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_msResolvedEntry.DownloadUrl));
        }
        catch { }
    }

    private void MsCopyLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(_msResolvedEntry.DownloadUrl);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            StatusInfoBar.Title = "已复制";
            StatusInfoBar.Message = "下载链接已复制到剪贴板";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.IsOpen = true;
        }
        catch { }
    }
}
