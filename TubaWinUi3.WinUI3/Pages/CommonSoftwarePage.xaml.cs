using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class CommonSoftwarePage : Page
{
    private List<CatalogCategory> _categories = [];
    private string _selectedCategory = "";
    private string _searchText = "";
    private bool _installing;

    public CommonSoftwarePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var available = await WingetService.IsWingetAvailableAsync();
        if (!available)
        {
            var errDialog = new ContentDialog
            {
                Title = "winget 不可用",
                Content = "未检测到 winget，软件安装功能将不可用。你可以从微软商店安装「应用安装程序」以启用 winget。",
                CloseButtonText = "知道了",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await errDialog.ShowAsync();
        }

        _categories = PcSetupCatalogService.GetCatalog();
        BuildChips();
        BuildStats();
        BuildPackageList();
        RefreshSummary();
        _ = CheckInstalledStatusAsync();
    }

    #region Chips

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text?.Trim() ?? "";
        BuildPackageList();
    }

    private void BuildChips()
    {
        CategoryChipsPanel.Children.Clear();
        CategoryChipsPanel.Children.Add(CreateChip("全部", ""));
        foreach (var cat in _categories)
            CategoryChipsPanel.Children.Add(CreateChip(cat.Name, cat.Name));
    }

    private Border CreateChip(string text, string tag)
    {
        var isSelected = tag == _selectedCategory;
        var chip = new Border
        {
            Tag = tag,
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(14, 6, 14, 6),
            Background = isSelected
                ? Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush ?? new SolidColorBrush(ThemeColors.AccentBlue)
                : new SolidColorBrush(ThemeColors.SubtleBg),
            BorderBrush = isSelected
                ? new SolidColorBrush(Colors.Transparent)
                : new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = isSelected ? new SolidColorBrush(Colors.White) : new SolidColorBrush(ThemeColors.SecondaryText)
            }
        };
        chip.PointerEntered += (_, _) =>
        {
            if (chip.Tag as string != _selectedCategory)
                chip.Background = new SolidColorBrush(ThemeColors.SubtleBgHover);
        };
        chip.PointerExited += (_, _) =>
        {
            if (chip.Tag as string != _selectedCategory)
                chip.Background = new SolidColorBrush(ThemeColors.SubtleBg);
        };
        chip.PointerPressed += (_, _) =>
        {
            _selectedCategory = chip.Tag as string ?? "";
            foreach (var child in CategoryChipsPanel.Children)
            {
                if (child is Border b && b.Tag is string t)
                {
                    b.Background = t == _selectedCategory
                        ? Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush ?? new SolidColorBrush(ThemeColors.AccentBlue)
                        : new SolidColorBrush(ThemeColors.SubtleBg);
                    b.BorderBrush = t == _selectedCategory
                        ? new SolidColorBrush(Colors.Transparent)
                        : new SolidColorBrush(ThemeColors.BorderColor);
                    if (b.Child is TextBlock tb)
                    {
                        tb.Foreground = t == _selectedCategory ? new SolidColorBrush(Colors.White) : new SolidColorBrush(ThemeColors.SecondaryText);
                        tb.FontWeight = t == _selectedCategory ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
                    }
                }
            }
            BuildPackageList();
        };
        return chip;
    }

    #endregion

    #region Package List

    private void BuildPackageList()
    {
        PackageList.Children.Clear();
        var shown = 0;

        foreach (var cat in _categories)
        {
            if (!string.IsNullOrEmpty(_selectedCategory) && cat.Name != _selectedCategory)
                continue;

            var catPackages = cat.Packages.Where(MatchesSearch).ToList();
            var subCatPackages = cat.SubCategories.SelectMany(s => s.Packages.Select(p => (Sub: s, Pkg: p)))
                .Where(x => MatchesSearch(x.Pkg)).ToList();

            if (catPackages.Count == 0 && subCatPackages.Count == 0) continue;

            var header = BuildGroupHeader($"{cat.Glyph}  {cat.Name}", catPackages.Count + subCatPackages.Count);
            PackageList.Children.Add(header);
            shown += catPackages.Count + subCatPackages.Count;

            foreach (var pkg in catPackages)
            {
                PackageList.Children.Add(CreatePackageRow(pkg, cat.Glyph));
                shown++;
            }

            foreach (var sub in cat.SubCategories)
            {
                var subPkgs = sub.Packages.Where(MatchesSearch).ToList();
                if (subPkgs.Count == 0) continue;
                PackageList.Children.Add(BuildSubHeader($"      {sub.Name}"));
                foreach (var pkg in subPkgs)
                {
                    PackageList.Children.Add(CreatePackageRow(pkg, cat.Glyph));
                    shown++;
                }
            }
        }

        if (shown == 0)
        {
            PackageList.Children.Add(new TextBlock
            {
                Text = "没有找到匹配的软件，试试其他关键词或分类。",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                Margin = new Thickness(8, 16, 0, 0)
            });
        }
    }

    private bool MatchesSearch(CatalogPackage pkg)
    {
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        return pkg.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
               (pkg.Desc ?? "").Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private Border BuildGroupHeader(string text, int count)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 2) };
        header.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromArgb(30, 96, 165, 250)),
            Child = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                Foreground = new SolidColorBrush(ThemeColors.AccentBlue),
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        return new Border { Child = header };
    }

    private TextBlock BuildSubHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.AccentBlue),
            Margin = new Thickness(8, 8, 0, 2)
        };
    }

    private Border CreatePackageRow(CatalogPackage pkg, string categoryGlyph)
    {
        var cb = new CheckBox
        {
            IsChecked = pkg.IsSelected,
            Tag = pkg.Id,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        cb.Checked += (_, _) => { pkg.IsSelected = true; RefreshSummary(); };
        cb.Unchecked += (_, _) => { pkg.IsSelected = false; RefreshSummary(); };

        var iconBorder = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            Child = new FontIcon
            {
                Glyph = categoryGlyph,
                FontSize = 15,
                Foreground = new SolidColorBrush(ThemeColors.AccentBlue)
            }
        };

        var nameBlock = new TextBlock
        {
            Text = pkg.Name,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var descBlock = new TextBlock
        {
            Text = pkg.Desc ?? "",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var nameStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(nameBlock);
        nameStack.Children.Add(descBlock);

        var badgePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (pkg.IsRecommended)
        {
            badgePanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromArgb(50, 251, 191, 36)),
                Child = new TextBlock
                {
                    Text = "推荐",
                    FontSize = 10.5,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(ThemeColors.AccentOrange)
                }
            });
        }
        if (pkg.State == WingetInstallState.Installed)
        {
            badgePanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromArgb(50, 74, 222, 128)),
                Child = new TextBlock
                {
                    Text = "已安装",
                    FontSize = 10.5,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(ThemeColors.AccentGreen)
                }
            });
        }

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        Grid.SetColumn(cb, 0);
        Grid.SetColumn(iconBorder, 1);
        Grid.SetColumn(nameStack, 2);
        Grid.SetColumn(badgePanel, 3);
        grid.Children.Add(cb);
        grid.Children.Add(iconBorder);
        grid.Children.Add(nameStack);
        grid.Children.Add(badgePanel);

        var row = new Border
        {
            Tag = pkg.Id,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Child = grid
        };
        row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(ThemeColors.RowHover);
        row.PointerExited += (_, _) => row.Background = new SolidColorBrush(ThemeColors.CardBg);
        return row;
    }

    #endregion

    #region Stats

    private void BuildStats()
    {
        var all = GetAllPackages();
        var total = all.Count;
        var installed = all.Count(p => p.State == WingetInstallState.Installed);
        var selected = all.Count(p => p.IsSelected && p.State != WingetInstallState.Installed);
        var recommended = all.Count(p => p.IsRecommended);

        StatsPanel.ColumnDefinitions.Clear();
        StatsPanel.Children.Clear();
        StatsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StatsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StatsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StatsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddStatCard(0, "可用软件", total, ThemeColors.AccentBlue);
        AddStatCard(1, "已安装", installed, ThemeColors.AccentGreen);
        AddStatCard(2, "待安装", selected, ThemeColors.AccentOrange);
        AddStatCard(3, "精选推荐", recommended, ThemeColors.AccentPurple);
    }

    private void AddStatCard(int column, string label, int value, Color accent)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = value.ToString(),
                        FontSize = 20,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(accent)
                    },
                    new TextBlock
                    {
                        Text = label,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(ThemeColors.DimText),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        Grid.SetColumn(card, column);
        StatsPanel.Children.Add(card);
    }

    private void RefreshSummary()
    {
        var all = GetAllPackages();
        var selected = all.Count(p => p.IsSelected && p.State != WingetInstallState.Installed);
        SummaryText.Text = selected > 0
            ? $"已选择 {selected} 个软件待安装"
            : "勾选需要安装的软件，点击「一键安装」批量安装";
        InstallBtn.IsEnabled = selected > 0 && !_installing;
    }

    private List<CatalogPackage> GetAllPackages()
    {
        var list = new List<CatalogPackage>();
        foreach (var cat in _categories)
        {
            list.AddRange(cat.Packages);
            foreach (var sub in cat.SubCategories) list.AddRange(sub.Packages);
        }
        return list;
    }

    #endregion

    #region Detection

    private async Task CheckInstalledStatusAsync()
    {
        var allPkgs = GetAllPackages();
        if (allPkgs.Count == 0) return;

        DetectionBar.Visibility = Visibility.Visible;
        DetectionProgress.Maximum = allPkgs.Count;

        try
        {
            await PcSetupCatalogService.CheckInstalledStatusAsync(_categories,
                new Progress<(int Done, int Total, string Name)>(p =>
                {
                    DetectionText.Text = $"正在检测: {p.Name}";
                    DetectionProgress.Value = p.Done;
                }));
            BuildPackageList();
            BuildStats();
            var installed = allPkgs.Count(p => p.State == WingetInstallState.Installed);
            DetectionText.Text = $"检测完成: {installed} 个已安装";
            DetectionRing.IsActive = false;
        }
        catch (Exception ex)
        {
            DetectionText.Text = $"检测失败: {ex.Message}";
            DetectionRing.IsActive = false;
        }

        await Task.Delay(800);
        DetectionBar.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Selection

    private void SelectRecommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var pkg in GetAllPackages())
            pkg.IsSelected = pkg.IsRecommended && pkg.State != WingetInstallState.Installed;
        BuildPackageList();
        BuildStats();
        RefreshSummary();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var pkg in GetAllPackages())
            pkg.IsSelected = pkg.State != WingetInstallState.Installed;
        BuildPackageList();
        BuildStats();
        RefreshSummary();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var pkg in GetAllPackages()) pkg.IsSelected = false;
        BuildPackageList();
        BuildStats();
        RefreshSummary();
    }

    #endregion

    #region Install

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;

        var actions = PcSetupCatalogService.ToInstallActions(_categories)
            .Where(a => a.IsSelected).ToList();
        if (actions.Count == 0) return;

        var needsAdmin = actions.Any(a => a.RequiresAdmin);
        if (needsAdmin && !IsRunningAsAdmin())
        {
            var adminDialog = new ContentDialog
            {
                Title = "需要管理员权限",
                Content = "部分软件需要管理员权限安装。\n\n建议以管理员身份重新运行本应用后再试。",
                CloseButtonText = "知道了",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await adminDialog.ShowAsync();
        }

        _installing = true;
        InstallBtn.IsEnabled = false;

        var cts = new CancellationTokenSource();
        var (dialog, rowUpdaters) = BuildInstallDialog(actions, cts);
        var showTask = dialog.ShowAsync();

        var successCount = 0;
        var failCount = 0;
        var doneCount = 0;

        foreach (var action in actions)
        {
            if (cts.IsCancellationRequested) break;
            if (rowUpdaters.TryGetValue(action.Id, out var running))
                running.Text = "正在安装...";
            UpdateInstallProgress(dialog, doneCount, actions.Count, action.Name, running: true);
            var result = await action.ExecuteAsync(
                new Progress<string>(line =>
                {
                    if (rowUpdaters.TryGetValue(action.Id, out var row))
                        row.Text = line;
                }), cts.Token);
            doneCount++;
            if (result.Success) successCount++;
            else failCount++;
            if (rowUpdaters.TryGetValue(action.Id, out var status))
                status.Text = result.Success ? "✔ 完成" : $"✘ {result.Message}";
            UpdateInstallProgress(dialog, doneCount, actions.Count, action.Name, running: false);
        }

        dialog.Hide();
        await showTask;
        cts.Dispose();

        _installing = false;
        InstallBtn.IsEnabled = true;

        var summary = $"安装完成：成功 {successCount} 个";
        if (failCount > 0) summary += $"，失败 {failCount} 个";
        var doneDialog = new ContentDialog
        {
            Title = successCount > 0 ? "安装完成" : "安装结果",
            Content = summary,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await doneDialog.ShowAsync();

        await CheckInstalledStatusAsync();
        BuildStats();
        RefreshSummary();
    }

    private (ContentDialog Dialog, Dictionary<string, TextBlock> StatusMap) BuildInstallDialog(
        List<WingetInstallAction> actions, CancellationTokenSource cts)
    {
        var statusMap = new Dictionary<string, TextBlock>();
        var rows = new StackPanel { Spacing = 4 };
        foreach (var action in actions)
        {
            var status = new TextBlock
            {
                Text = "排队中",
                FontSize = 11,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusMap[action.Id] = status;

            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(ThemeColors.CardBg),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(1),
                Child = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }
                    },
                    Children =
                    {
                        new TextBlock
                        {
                            Text = action.Name,
                            FontSize = 12.5,
                            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        status
                    }
                }
            };
            Grid.SetColumn(status, 1);
            rows.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 320,
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var cancelBtn = new Button
        {
            Content = "取消剩余安装",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0)
        };
        cancelBtn.Click += (_, _) => cts.Cancel();

        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new ProgressBar { IsIndeterminate = false, Value = 0, Maximum = actions.Count },
                new TextBlock
                {
                    Text = $"共 {actions.Count} 个软件，正在安装...",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(ThemeColors.DimText)
                },
                scroll,
                cancelBtn
            }
        };

        var dialog = new ContentDialog
        {
            Title = "正在安装软件",
            Content = panel,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        return (dialog, statusMap);
    }

    private void UpdateInstallProgress(ContentDialog dialog, int done, int total, string name, bool running)
    {
        if (dialog.Content is StackPanel panel &&
            panel.Children[0] is ProgressBar bar &&
            panel.Children[1] is TextBlock info)
        {
            bar.Maximum = total;
            bar.Value = done;
            info.Text = running
                ? $"正在安装: {name} ({done}/{total})"
                : $"已完成: {name} ({done}/{total})";
        }
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    #endregion
}
