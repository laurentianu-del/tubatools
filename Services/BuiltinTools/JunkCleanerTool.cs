using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class JunkCleanerTool : IBuiltinTool
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    public string Id => "junk-cleaner";
    public string Name => "垃圾清理";
    public string Description => "扫描并清理系统临时文件、浏览器缓存、回收站等垃圾文件。";
    public string Glyph => "\uE74D";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.ProgressTask;

    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color AccentYellow = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);

    private List<JunkCategory>? _categories;
    private List<AiJunkSuggestion>? _aiSuggestions;
    private CancellationTokenSource? _cts;
    private bool _aiFullScan;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = context.CreateDialog("垃圾清理");
        dialog.Resources["ContentDialogMaxWidth"] = 920;
        dialog.Resources["ContentDialogMaxHeight"] = 720;
        dialog.Closing += (_, _) => _cts?.Cancel();

        var content = BuildDialogContent();
        dialog.Content = content;

        await dialog.ShowAsync();
    }

    private StackPanel BuildDialogContent()
    {
        var totalSizeText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentBlue) };
        var totalFilesText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentGreen) };
        var categoryCountText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold };

        var sizeCard = MakeStatCard("总大小", totalSizeText, "\uEDA2", AccentBlue);
        var filesCard = MakeStatCard("项目数", totalFilesText, "\uE8C8", AccentGreen);
        var catCard = MakeStatCard("分类", categoryCountText, "\uE7F4", AccentPurple);

        var statsGrid = new Grid { ColumnSpacing = 10 };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.Children.Add(sizeCard); Grid.SetColumn(sizeCard, 0);
        statsGrid.Children.Add(filesCard); Grid.SetColumn(filesCard, 1);
        statsGrid.Children.Add(catCard); Grid.SetColumn(catCard, 2);

        var scanBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "快速扫描" }
                }
            }
        };

        var aiQuickScanBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE945", FontSize = 12 },
                    new TextBlock { Text = "AI 快速扫描" }
                }
            }
        };

        var aiFullScanBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE721", FontSize = 12 },
                    new TextBlock { Text = "AI 完全扫描" }
                }
            }
        };

        var cleanBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                    new TextBlock { Text = "清理" }
                }
            },
            IsEnabled = false
        };

        var selectAllBtn = new Button { Content = "全选", Padding = new Thickness(8, 4, 8, 4) };
        var deselectAllBtn = new Button { Content = "取消全选", Padding = new Thickness(8, 4, 8, 4) };
        var exportBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE896", FontSize = 12 },
                    new TextBlock { Text = "导出报告" }
                }
            },
            Visibility = Visibility.Collapsed
        };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(scanBtn);
        actionBar.Children.Add(aiQuickScanBtn);
        actionBar.Children.Add(aiFullScanBtn);
        actionBar.Children.Add(cleanBtn);
        actionBar.Children.Add(selectAllBtn);
        actionBar.Children.Add(deselectAllBtn);
        actionBar.Children.Add(exportBtn);

        var categoryList = new StackPanel { Spacing = 8 };
        var listScroll = new ScrollViewer
        {
            Content = categoryList,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var aiLogList = new StackPanel { Spacing = 4 };
        var aiLogScroll = new ScrollViewer
        {
            Content = aiLogList,
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var loadingRing = new ProgressRing { Width = 28, Height = 28, IsActive = true };
        var loadingText = new TextBlock { Text = "正在扫描垃圾文件...", FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText), VerticalAlignment = VerticalAlignment.Center };
        var loadingPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Padding = new Thickness(0, 12, 0, 12),
            Visibility = Visibility.Collapsed,
            Children = { loadingRing, loadingText }
        };

        var confirmText = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(AccentYellow),
            TextWrapping = TextWrapping.Wrap
        };

        var confirmYesBtn = new Button { Content = "确认清理" };
        var confirmNoBtn = new Button { Content = "取消" };

        var confirmPanel = new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Background = new SolidColorBrush(Color.FromArgb(30, AccentYellow.R, AccentYellow.G, AccentYellow.B)),
            BorderBrush = new SolidColorBrush(AccentYellow),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    confirmText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            confirmYesBtn,
                            confirmNoBtn
                        }
                    }
                }
            }
        };

        var aiWarningTip = new InfoBar
        {
            Title = "AI 扫描结果仅供参考",
            Message = "AI 分析可能存在误判，请仔细核对每项内容后再清理，避免误删重要文件。",
            Severity = InfoBarSeverity.Warning,
            IsOpen = true,
            IsClosable = true,
            Visibility = Visibility.Collapsed
        };

        var contentGrid = new Grid { RowSpacing = 10 };
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        contentGrid.Children.Add(statsGrid); Grid.SetRow(statsGrid, 0);
        contentGrid.Children.Add(actionBar); Grid.SetRow(actionBar, 1);
        contentGrid.Children.Add(confirmPanel); Grid.SetRow(confirmPanel, 2);
        contentGrid.Children.Add(aiWarningTip); Grid.SetRow(aiWarningTip, 3);
        contentGrid.Children.Add(loadingPanel); Grid.SetRow(loadingPanel, 4);
        contentGrid.Children.Add(aiLogScroll); Grid.SetRow(aiLogScroll, 5);
        contentGrid.Children.Add(listScroll); Grid.SetRow(listScroll, 6);

        var resultText = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentGreen),
            Visibility = Visibility.Collapsed
        };

        var root = new StackPanel { Spacing = 14, MaxWidth = 880 };
        root.Children.Add(new TextBlock
        {
            Text = "扫描并清理系统临时文件、浏览器缓存、回收站等垃圾文件，释放磁盘空间",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        root.Children.Add(contentGrid);
        root.Children.Add(resultText);

        root.Tag = new JunkCleanerState
        {
            TotalSizeText = totalSizeText,
            TotalFilesText = totalFilesText,
            CategoryCountText = categoryCountText,
            ScanBtn = scanBtn,
            AiQuickScanBtn = aiQuickScanBtn,
            AiFullScanBtn = aiFullScanBtn,
            CleanBtn = cleanBtn,
            SelectAllBtn = selectAllBtn,
            DeselectAllBtn = deselectAllBtn,
            ExportBtn = exportBtn,
            CategoryList = categoryList,
            ListScroll = listScroll,
            LoadingRing = loadingRing,
            LoadingPanel = loadingPanel,
            LoadingText = loadingText,
            ResultText = resultText,
            AiLogList = aiLogList,
            AiLogScroll = aiLogScroll,
            ConfirmPanel = confirmPanel,
            ConfirmText = confirmText,
            ConfirmYesBtn = confirmYesBtn,
            ConfirmNoBtn = confirmNoBtn,
            AiWarningTip = aiWarningTip
        };

        scanBtn.Click += async (_, _) =>
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _aiSuggestions = null;
            exportBtn.Visibility = Visibility.Collapsed;
            await ScanStandardAsync(root, _cts.Token);
        };

        aiQuickScanBtn.Click += async (_, _) =>
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _aiFullScan = false;
            await ScanAiAsync(root, _cts.Token);
        };

        aiFullScanBtn.Click += async (_, _) =>
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _aiFullScan = true;
            await ScanAiAsync(root, _cts.Token);
        };

        cleanBtn.Click += async (_, _) =>
        {
            if (_aiSuggestions is not null)
            {
                await CleanAiAsync(root);
            }
            else if (_categories is not null)
            {
                await CleanStandardAsync(root);
            }
        };

        selectAllBtn.Click += (_, _) =>
        {
            if (_aiSuggestions is not null)
            {
                foreach (var s in _aiSuggestions) s.Selected = true;
                RenderAiSuggestions(root);
            }
            else if (_categories is not null)
            {
                foreach (var c in _categories) c.Selected = true;
                RenderCategories(root);
            }
        };

        deselectAllBtn.Click += (_, _) =>
        {
            if (_aiSuggestions is not null)
            {
                foreach (var s in _aiSuggestions) s.Selected = false;
                RenderAiSuggestions(root);
            }
            else if (_categories is not null)
            {
                foreach (var c in _categories) c.Selected = false;
                RenderCategories(root);
            }
        };

        exportBtn.Click += async (_, _) =>
        {
            if (_aiSuggestions is null) return;
            await ExportAiReportAsync(root);
        };

        return root;
    }

    private async Task ScanStandardAsync(StackPanel root, CancellationToken ct)
    {
        var state = GetState(root);
        if (state is null) return;

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.LoadingText.Text = "正在扫描垃圾文件...";
        state.AiLogScroll.Visibility = Visibility.Collapsed;
        state.AiLogList.Children.Clear();
        state.CategoryList.Children.Clear();
        state.ListScroll.Visibility = Visibility.Visible;
        state.CleanBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.AiQuickScanBtn.IsEnabled = false;
        state.AiFullScanBtn.IsEnabled = false;
        state.ResultText.Visibility = Visibility.Collapsed;

        _categories = await JunkCleanerService.ScanAsync(ct);

        RefreshUI(root);
        RenderCategories(root);

        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.LoadingRing.IsActive = false;
        state.CleanBtn.IsEnabled = _categories.Any(c => c.SizeBytes > 0);
        state.ScanBtn.IsEnabled = true;
        state.AiQuickScanBtn.IsEnabled = true;
        state.AiFullScanBtn.IsEnabled = true;
    }

    private async Task ScanAiAsync(StackPanel root, CancellationToken ct)
    {
        var state = GetState(root);
        if (state is null) return;

        if (!AiService.IsConfigured)
        {
            var tipDialog = new ContentDialog
            {
                Title = "AI 服务未配置",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "请先在设置中配置 AI 服务（API 地址、模型名和 API Key），才能使用 AI 智能扫描功能。",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = "设置路径：设置 → AI 服务",
                            Opacity = 0.68,
                            FontSize = 13
                        }
                    }
                },
                CloseButtonText = "关闭",
                XamlRoot = root.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await tipDialog.ShowAsync();
            return;
        }

        var scanLabel = _aiFullScan ? "AI 完全扫描" : "AI 快速扫描";

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.LoadingText.Text = $"{scanLabel}已启动，正在收集系统信息...";
        state.AiLogList.Children.Clear();
        state.AiLogScroll.Visibility = Visibility.Visible;
        state.CategoryList.Children.Clear();
        state.ListScroll.Visibility = Visibility.Collapsed;
        state.CleanBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.AiQuickScanBtn.IsEnabled = false;
        state.AiFullScanBtn.IsEnabled = false;
        state.ExportBtn.Visibility = Visibility.Collapsed;
        state.ResultText.Visibility = Visibility.Collapsed;

        _categories = null;

        AppendAiLog(state, "开始", $"{scanLabel}已启动，正在收集系统信息...", AccentBlue);

        var progress = new Progress<AiAnalyzerProgress>(p =>
        {
            state.LoadingText.Text = p.Status;
            if (p.Log is not null)
            {
                var color = p.Log switch
                {
                    var l when l.Contains("[工具调用") => AccentYellow,
                    var l when l.Contains("[工具结果") => AccentGreen,
                    var l when l.StartsWith("[AI 思考]") => AccentPurple,
                    var l when l.StartsWith("[完成]") => AccentGreen,
                    _ => ThemeColors.PrimaryText
                };
                var label = p.Log switch
                {
                    var l when l.Contains("[工具调用") => "工具",
                    var l when l.Contains("[工具结果") => "结果",
                    var l when l.StartsWith("[AI 思考]") => "思考",
                    var l when l.StartsWith("[完成]") => "完成",
                    var l when l.StartsWith("[第") => "等待",
                    _ => "信息"
                };
                AppendAiLog(state, label, p.Log, color);
            }
        });

        List<AiChatMessage>? continuationMessages = null;

        while (true)
        {
            try
            {
                _aiSuggestions = await AiJunkAnalyzerService.AnalyzeAsync(progress, ct, continuationMessages, _aiFullScan);

                if (_aiSuggestions.Count == 0)
                {
                    state.ResultText.Text = "AI 未发现可清理的垃圾文件";
                    state.ResultText.Foreground = new SolidColorBrush(AccentGreen);
                    state.ResultText.Visibility = Visibility.Visible;
                }
                else
                {
                    AppendAiLog(state, "完成", $"分析完成，共发现 {_aiSuggestions.Count} 个可清理项目", AccentGreen);

                    state.AiLogScroll.Visibility = Visibility.Collapsed;
                    state.ListScroll.Visibility = Visibility.Visible;
                    RenderAiSuggestions(root);
                    state.CleanBtn.IsEnabled = _aiSuggestions.Any(s => s.Selected);
                    state.ExportBtn.Visibility = Visibility.Visible;
                }
                break;
            }
            catch (AiMaxRoundsReachedException ex)
            {
                AppendAiLog(state, "暂停", $"已达到 {ex.RoundsCompleted} 轮上限", AccentYellow);

                state.LoadingPanel.Visibility = Visibility.Collapsed;
                state.LoadingRing.IsActive = false;

                state.ConfirmText.Text = $"AI 已分析 {ex.RoundsCompleted} 轮，尚未输出最终报告。AI 可能还在探查更多目录。你可以选择继续让 AI 分析，或者停止并查看当前已有的探查记录。";
                state.ConfirmYesBtn.Content = "继续分析";
                state.ConfirmNoBtn.Content = "停止";
                state.ConfirmPanel.Visibility = Visibility.Visible;

                var roundTcs = new TaskCompletionSource<int>();
                void OnRoundYes(object s, RoutedEventArgs e) { roundTcs.TrySetResult(1); }
                void OnRoundNo(object s, RoutedEventArgs e) { roundTcs.TrySetResult(2); }

                state.ConfirmYesBtn.Click += OnRoundYes;
                state.ConfirmNoBtn.Click += OnRoundNo;

                var roundChoice = await roundTcs.Task;

                state.ConfirmYesBtn.Click -= OnRoundYes;
                state.ConfirmNoBtn.Click -= OnRoundNo;
                state.ConfirmPanel.Visibility = Visibility.Collapsed;

                if (roundChoice == 1)
                {
                    continuationMessages = ex.Messages;
                    AppendAiLog(state, "继续", "用户选择继续分析...", AccentBlue);
                    state.LoadingPanel.Visibility = Visibility.Visible;
                    state.LoadingRing.IsActive = true;
                    state.LoadingText.Text = "继续分析...";
                    continue;
                }

                AppendAiLog(state, "停止", "用户选择停止分析", AccentYellow);
                state.ResultText.Text = "AI 分析已停止（达到轮次上限）";
                state.ResultText.Foreground = new SolidColorBrush(AccentYellow);
                state.ResultText.Visibility = Visibility.Visible;
                break;
            }
            catch (OperationCanceledException)
            {
                AppendAiLog(state, "取消", "AI 扫描已取消", AccentRed);
                state.ResultText.Text = "AI 扫描已取消";
                state.ResultText.Foreground = new SolidColorBrush(ThemeColors.DimText);
                state.ResultText.Visibility = Visibility.Visible;
                break;
            }
            catch (Exception ex)
            {
                AppendAiLog(state, "错误", $"AI 扫描失败：{ex.Message}", AccentRed);
                state.ResultText.Text = $"AI 扫描失败：{ex.Message}";
                state.ResultText.Foreground = new SolidColorBrush(AccentRed);
                state.ResultText.Visibility = Visibility.Visible;
                break;
            }
        }

        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.LoadingRing.IsActive = false;
        state.ScanBtn.IsEnabled = true;
        state.AiQuickScanBtn.IsEnabled = true;
        state.AiFullScanBtn.IsEnabled = true;
    }

    private static void AppendAiLog(JunkCleanerState state, string label, string text, Color accent)
    {
        var dimAccent = Color.FromArgb(30, accent.R, accent.G, accent.B);

        var badge = new Border
        {
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(dimAccent),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(accent),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };

        var content = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            TextWrapping = TextWrapping.Wrap,
            Opacity = label is "结果" or "思考" ? 0.85 : 1.0
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Thickness(0, 2, 0, 2)
        };
        row.Children.Add(badge);
        row.Children.Add(content);

        state.AiLogList.Children.Add(row);

        state.AiLogScroll.ChangeView(null, state.AiLogScroll.ScrollableHeight, null);
    }

    private async Task CleanAiAsync(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _aiSuggestions is null) return;

        var selected = _aiSuggestions.Where(s => s.Selected).ToList();
        if (selected.Count == 0) return;

        var totalSize = selected.Sum(s => s.SizeBytes);
        var sizeInfo = totalSize > 0 ? $"（共 {AiJunkAnalyzerService.FormatSize(totalSize)}）" : "";
        state.ConfirmText.Text = $"即将清理 {selected.Count} 个项目{sizeInfo}，此操作不可撤销。确定继续？";
        state.ConfirmPanel.Visibility = Visibility.Visible;

        var tcs = new TaskCompletionSource<bool>();

        void OnYes(object s, RoutedEventArgs e)
        {
            tcs.TrySetResult(true);
        }

        void OnNo(object s, RoutedEventArgs e)
        {
            tcs.TrySetResult(false);
        }

        state.ConfirmYesBtn.Click += OnYes;
        state.ConfirmNoBtn.Click += OnNo;

        var confirmed = await tcs.Task;

        state.ConfirmYesBtn.Click -= OnYes;
        state.ConfirmNoBtn.Click -= OnNo;
        state.ConfirmPanel.Visibility = Visibility.Collapsed;

        if (!confirmed) return;

        state.CleanBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.AiQuickScanBtn.IsEnabled = false;
        state.AiFullScanBtn.IsEnabled = false;
        state.ResultText.Visibility = Visibility.Collapsed;

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.LoadingText.Text = "正在清理文件...";

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var cleanProgress = new Progress<CleanProgress>(p =>
        {
            root.DispatcherQueue.TryEnqueue(() =>
            {
                state.LoadingText.Text = $"正在清理 ({p.Current}/{p.Total})：{Path.GetFileName(p.CurrentPath)}";
            });
        });

        var cleaned = await Task.Run(() => AiJunkAnalyzerService.CleanSelected(_aiSuggestions, cleanProgress, _cts.Token));

        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.LoadingRing.IsActive = false;

        state.ResultText.Text = $"清理完成！释放了 {AiJunkAnalyzerService.FormatSize(cleaned)} 空间";
        state.ResultText.Foreground = new SolidColorBrush(AccentGreen);
        state.ResultText.Visibility = Visibility.Visible;

        _aiSuggestions = _aiSuggestions.Where(s =>
        {
            if (!s.Selected) return true;
            return Directory.Exists(s.Path) || File.Exists(s.Path);
        }).ToList();

        RenderAiSuggestions(root);
        state.ScanBtn.IsEnabled = true;
        state.AiQuickScanBtn.IsEnabled = true;
        state.AiFullScanBtn.IsEnabled = true;
        state.CleanBtn.IsEnabled = _aiSuggestions.Any(s => s.Selected);
    }

    private async Task ExportAiReportAsync(StackPanel root)
    {
        if (_aiSuggestions is null) return;

        var json = AiJunkAnalyzerService.ExportReportJson(_aiSuggestions);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var savePath = PickSaveFile(hwnd, "导出 AI 清理报告", "JSON 文件\0*.json\0所有文件\0*.*\0\0", "AiJunkReport.json", "json");
        if (string.IsNullOrWhiteSpace(savePath)) return;

        if (!savePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            savePath += ".json";

        try
        {
            await Task.Run(() => File.WriteAllText(savePath, json, System.Text.Encoding.UTF8));
            var state = GetState(root);
            if (state is not null)
            {
                state.ResultText.Text = $"报告已导出：{savePath}";
                state.ResultText.Foreground = new SolidColorBrush(AccentBlue);
                state.ResultText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            var state = GetState(root);
            if (state is not null)
            {
                state.ResultText.Text = $"导出失败：{ex.Message}";
                state.ResultText.Foreground = new SolidColorBrush(AccentRed);
                state.ResultText.Visibility = Visibility.Visible;
            }
        }
    }

    private static string? PickSaveFile(IntPtr hwnd, string title, string filter, string defaultFileName, string defaultExtension)
    {
        var buffer = defaultFileName + new string('\0', 1024 - defaultFileName.Length);
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = hwnd,
            lpstrFilter = filter,
            lpstrFile = buffer,
            nMaxFile = 1024,
            lpstrTitle = title,
            lpstrDefExt = defaultExtension,
            Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        return GetSaveFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    private async Task CleanStandardAsync(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _categories is null) return;

        state.CleanBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.AiQuickScanBtn.IsEnabled = false;
        state.AiFullScanBtn.IsEnabled = false;
        state.ResultText.Visibility = Visibility.Collapsed;

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.LoadingText.Text = "正在清理文件...";

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var progress = new Progress<CleanProgress>(p =>
        {
            root.DispatcherQueue.TryEnqueue(() =>
            {
                state.LoadingText.Text = $"正在清理 ({p.Current}/{p.Total})：{Path.GetFileName(p.CurrentPath)}";
            });
        });

        var cleaned = await Task.Run(() => JunkCleanerService.Clean(_categories, progress, _cts.Token));

        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.LoadingRing.IsActive = false;

        state.ResultText.Text = $"清理完成！释放了 {JunkCleanerService.FormatSize(cleaned)} 空间";
        state.ResultText.Visibility = Visibility.Visible;

        RefreshUI(root);
        RenderCategories(root);
        state.ScanBtn.IsEnabled = true;
        state.AiQuickScanBtn.IsEnabled = true;
        state.AiFullScanBtn.IsEnabled = true;
    }

    private void RefreshUI(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _categories is null) return;

        var totalSize = _categories.Sum(c => c.SizeBytes);
        var totalFiles = _categories.Sum(c => c.FileCount);
        state.TotalSizeText.Text = JunkCleanerService.FormatSize(totalSize);
        state.TotalFilesText.Text = totalFiles.ToString();
        state.CategoryCountText.Text = _categories.Count.ToString();
    }

    private void RefreshAiUI(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _aiSuggestions is null) return;

        var totalSize = _aiSuggestions.Where(s => s.Selected).Sum(s => s.SizeBytes);
        state.TotalSizeText.Text = AiJunkAnalyzerService.FormatSize(totalSize);
        state.TotalFilesText.Text = _aiSuggestions.Count(s => s.Selected).ToString();
        state.CategoryCountText.Text = _aiSuggestions.Select(s => s.Category).Distinct().Count().ToString();
    }

    private void RenderCategories(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _categories is null) return;

        state.CategoryList.Children.Clear();
        state.AiWarningTip.Visibility = Visibility.Collapsed;

        foreach (var cat in _categories)
        {
            state.CategoryList.Children.Add(CreateCategoryRow(cat, root));
        }

        RefreshUI(root);
        state.CleanBtn.IsEnabled = _categories.Any(c => c.Selected && c.SizeBytes > 0);
    }

    private void RenderAiSuggestions(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _aiSuggestions is null) return;

        state.CategoryList.Children.Clear();
        state.AiWarningTip.Visibility = Visibility.Visible;

        foreach (var item in _aiSuggestions)
        {
            state.CategoryList.Children.Add(CreateAiSuggestionRow(item, root));
        }

        RefreshAiUI(root);
        state.CleanBtn.IsEnabled = _aiSuggestions.Any(s => s.Selected);
    }

    private Border CreateCategoryRow(JunkCategory cat, StackPanel root)
    {
        var accent = ParseHex(cat.ColorHex);
        var dimAccent = Color.FromArgb(26, accent.R, accent.G, accent.B);

        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(dimAccent),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = cat.Glyph }
        };

        var nameText = new TextBlock
        {
            Text = cat.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var descText = new TextBlock
        {
            Text = cat.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var sizeText = new TextBlock
        {
            Text = cat.FileCount > 0 ? JunkCleanerService.FormatSize(cat.SizeBytes) : "无文件",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(cat.FileCount > 0 ? accent : ThemeColors.DimText)
        };

        var countText = new TextBlock
        {
            Text = cat.FileCount > 0 ? $"{cat.FileCount} 个文件" : "",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var toggle = new ToggleSwitch
        {
            IsOn = cat.Selected,
            OnContent = "",
            OffContent = "",
            MinWidth = 76
        };
        toggle.Toggled += (_, _) =>
        {
            cat.Selected = toggle.IsOn;
            GetState(root)?.CleanBtn.IsEnabled = _categories?.Any(c => c.Selected && c.SizeBytes > 0) ?? false;
        };

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(descText);

        var sizePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        sizePanel.Children.Add(sizeText);
        sizePanel.Children.Add(countText);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(sizePanel); Grid.SetColumn(sizePanel, 2);
        grid.Children.Add(toggle); Grid.SetColumn(toggle, 3);

        return new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private Border CreateAiSuggestionRow(AiJunkSuggestion item, StackPanel root)
    {
        var riskColor = item.RiskLevel switch
        {
            "safe" => AccentGreen,
            "low" => AccentBlue,
            "medium" => AccentYellow,
            "high" => AccentRed,
            _ => AccentGreen
        };

        var riskLabel = item.RiskLevel switch
        {
            "safe" => "安全",
            "low" => "低风险",
            "medium" => "中风险",
            "high" => "高风险",
            _ => "安全"
        };

        var riskGlyph = item.RiskLevel switch
        {
            "safe" => "\uE73E",
            "low" => "\uE73E",
            "medium" => "\uE7BA",
            "high" => "\uE783",
            _ => "\uE73E"
        };

        var dimRisk = Color.FromArgb(26, riskColor.R, riskColor.G, riskColor.B);

        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(dimRisk),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(riskColor), Glyph = riskGlyph }
        };

        var pathText = new TextBlock
        {
            Text = item.Path,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var descText = new TextBlock
        {
            Text = item.Description,
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        };

        var reasonText = new TextBlock
        {
            Text = item.Reason,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(180, ThemeColors.DimText.R, ThemeColors.DimText.G, ThemeColors.DimText.B)),
            TextWrapping = TextWrapping.Wrap
        };

        var riskBadge = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(dimRisk),
            Child = new TextBlock
            {
                Text = riskLabel,
                FontSize = 11,
                Foreground = new SolidColorBrush(riskColor),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };

        var categoryBadge = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(20, AccentPurple.R, AccentPurple.G, AccentPurple.B)),
            Child = new TextBlock
            {
                Text = item.Category,
                FontSize = 11,
                Foreground = new SolidColorBrush(AccentPurple)
            }
        };

        var sizeText = new TextBlock
        {
            Text = item.SizeBytes > 0 ? AiJunkAnalyzerService.FormatSize(item.SizeBytes) : "--",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(item.SizeBytes > 0 ? riskColor : ThemeColors.DimText)
        };

        var sizeLabel = new TextBlock
        {
            Text = item.SizeBytes > 0 ? (Directory.Exists(item.Path) ? "文件夹" : "文件") : "",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var sizePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        sizePanel.Children.Add(sizeText);
        sizePanel.Children.Add(sizeLabel);

        var toggle = new ToggleSwitch
        {
            IsOn = item.Selected,
            OnContent = "",
            OffContent = "",
            MinWidth = 76
        };
        toggle.Toggled += (_, _) =>
        {
            item.Selected = toggle.IsOn;
            var st = GetState(root);
            if (st is not null)
            {
                st.CleanBtn.IsEnabled = _aiSuggestions?.Any(s => s.Selected) ?? false;
                st.TotalFilesText.Text = _aiSuggestions?.Count(s => s.Selected).ToString() ?? "0";
            }
        };

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(pathText);
        infoPanel.Children.Add(descText);
        infoPanel.Children.Add(reasonText);

        var badgePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        badgePanel.Children.Add(riskBadge);
        badgePanel.Children.Add(categoryBadge);

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(badgePanel); Grid.SetColumn(badgePanel, 2); Grid.SetRow(badgePanel, 0);
        grid.Children.Add(sizePanel); Grid.SetColumn(sizePanel, 3); Grid.SetRowSpan(sizePanel, 2);
        grid.Children.Add(toggle); Grid.SetColumn(toggle, 4); Grid.SetRow(toggle, 0); Grid.SetRowSpan(toggle, 2);

        return new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private static Color ParseHex(string hex)
    {
        var r = Convert.ToByte(hex[1..3], 16);
        var g = Convert.ToByte(hex[3..5], 16);
        var b = Convert.ToByte(hex[5..7], 16);
        return Color.FromArgb(255, r, g, b);
    }

    private static JunkCleanerState? GetState(StackPanel root) => root?.Tag as JunkCleanerState;

    private static Border MakeStatCard(string label, TextBlock value, string glyph, Color accent)
    {
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(value);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        grid.Children.Add(stack); Grid.SetColumn(stack, 1);

        return new Border
        {
            Padding = new Thickness(12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private sealed class JunkCleanerState
    {
        public TextBlock TotalSizeText = null!;
        public TextBlock TotalFilesText = null!;
        public TextBlock CategoryCountText = null!;
        public Button ScanBtn = null!;
        public Button AiQuickScanBtn = null!;
        public Button AiFullScanBtn = null!;
        public Button CleanBtn = null!;
        public Button SelectAllBtn = null!;
        public Button DeselectAllBtn = null!;
        public Button ExportBtn = null!;
        public StackPanel CategoryList = null!;
        public ScrollViewer ListScroll = null!;
        public ProgressRing LoadingRing = null!;
        public StackPanel LoadingPanel = null!;
        public TextBlock LoadingText = null!;
        public TextBlock ResultText = null!;
        public StackPanel AiLogList = null!;
        public ScrollViewer AiLogScroll = null!;
        public Border ConfirmPanel = null!;
        public TextBlock ConfirmText = null!;
        public Button ConfirmYesBtn = null!;
        public Button ConfirmNoBtn = null!;
        public InfoBar AiWarningTip = null!;
    }
}
