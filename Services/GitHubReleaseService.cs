using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed record GitHubAssetInfo(string Name, string OriginalUrl, long Size);

public sealed record GitHubReleaseInfo(
    string TagName,
    string? Name,
    string? Body,
    IReadOnlyList<GitHubAssetInfo> Assets);

public sealed record ProxySpeedResult(string Name, string? Url, double Ms, bool Ok);

public static class GitHubReleaseService
{
    private static readonly HttpClient _apiClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static GitHubReleaseService()
    {
        _apiClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-GitHubRelease");
    }

    public static readonly string[] GitHubProxies =
    [
        "https://gh-proxy.com/",
        "https://github-proxy.lixxing.top/",
        "https://y.whereisdoge.work/",
        "https://github-proxy.memory-echoes.cn/",
        "https://j.1lin.dpdns.org/",
        "https://github.cnxiaobai.com/",
        "https://gh.aaa.team/",
        "https://gh.jasonzeng.dev/",
        "https://gitproxy.mrhjx.cn/",
        "https://github.chenc.dev/",
        "https://ghproxy.053000.xyz/",
        "https://gh.monlor.com/",
        "https://proxy.yaoyaoling.net/",
        "https://gp.zkitefly.eu.org/",
        "https://gp.871201.xyz/",
        "https://fastgit.cc/",
        "https://gh.1k.ink/",
        "https://github.geekery.cn/",
        "https://hub.ddayh.com/",
        "https://jiashu.1win.eu.org/",
        "https://tvv.tw/",
        "https://ghp.keleyaa.com/",
        "https://github.788787.xyz/",
        "https://gh.con.sh/",
        "https://gh.bugdey.us.kg/",
        "https://ghproxy.xzhouqd.com/",
        "https://github.mlmle.cn/",
        "https://proxy.baguoyuyan.com/",
        "https://github.ihnic.com/",
        "https://gh.idayer.com/",
        "https://github.crdz.eu.org/",
        "https://ggg.clwap.dpdns.org/",
        "https://getgit.love8yun.eu.org/",
        "https://gh.996986.xyz/",
        "https://github.boringhex.top/",
        "https://gh.198962.xyz/",
        "https://gh.chjina.com/",
        "https://github.kkproxy.dpdns.org/",
        "https://ghproxy.mf-dust.dpdns.org/"
    ];

    public static string ProxyUrl(string proxyBase, string originalUrl)
    {
        var base_ = proxyBase.TrimEnd('/');
        return $"{base_}/{originalUrl}";
    }

    public static async Task<GitHubReleaseInfo?> FetchLatestReleaseAsync(
        string repo, CancellationToken ct = default)
    {
        return await FetchReleaseByTagAsync(repo, null, ct);
    }

    public static async Task<GitHubReleaseInfo?> FetchReleaseByTagAsync(
        string repo, string? tag, CancellationToken ct = default)
    {
        var apiUrl = tag is not null
            ? $"https://api.github.com/repos/{repo}/releases/tags/{tag}"
            : $"https://api.github.com/repos/{repo}/releases/latest";

        try
        {
            var json = await _apiClient.GetStringAsync(apiUrl, ct);
            return ParseReleaseJson(json);
        }
        catch
        {
            return null;
        }
    }

    public static GitHubReleaseInfo? ParseReleaseJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagEl)
                ? tagEl.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var nameEl)
                ? nameEl.GetString() : null;
            var body = root.TryGetProperty("body", out var bodyEl)
                ? bodyEl.GetString() : null;

            if (!root.TryGetProperty("assets", out var assetsEl))
                return new GitHubReleaseInfo(tagName, name, body, []);

            var assets = new List<GitHubAssetInfo>();
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var aName = asset.GetProperty("name").GetString() ?? "";
                var aUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                var aSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;
                assets.Add(new GitHubAssetInfo(aName, aUrl, aSize));
            }

            return new GitHubReleaseInfo(tagName, name, body, assets);
        }
        catch
        {
            return null;
        }
    }

    public static GitHubAssetInfo? FindBestAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch, AssetMatchStrategy strategy)
    {
        return strategy switch
        {
            AssetMatchStrategy.UniGetUI => FindUniGetUIAsset(assets, arch),
            AssetMatchStrategy.OptimizerDuck => FindOptimizerDuckAsset(assets, arch),
            AssetMatchStrategy.ContextMenuMgr => FindContextMenuMgrAsset(assets, arch),
            _ => FindGenericAsset(assets, arch)
        };
    }

    private static GitHubAssetInfo? FindUniGetUIAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch)
    {
        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Installer", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains($".{arch}.", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.Equals("UniGetUI.Installer.exe", StringComparison.OrdinalIgnoreCase) &&
                arch == "x64")
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Installer", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    private static GitHubAssetInfo? FindOptimizerDuckAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch)
    {
        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains($"-{arch}-", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Windows", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    private static GitHubAssetInfo? FindContextMenuMgrAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch)
    {
        foreach (var a in assets)
        {
            if (a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("self-contained", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("self-contained", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    private static GitHubAssetInfo? FindGenericAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch)
    {
        foreach (var a in assets)
        {
            if (a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    public static async Task<List<ProxySpeedResult>> TestProxiesAsync(
        string originalUrl, int maxConcurrent = 8, CancellationToken ct = default)
    {
        var results = new List<ProxySpeedResult>();
        var allUrls = new List<(string Name, string Url)>();

        allUrls.Add(("GitHub 直连", originalUrl));

        foreach (var proxy in GitHubProxies)
        {
            var name = new Uri(proxy).Host;
            allUrls.Add((name, ProxyUrl(proxy, originalUrl)));
        }

        var semaphore = new SemaphoreSlim(maxConcurrent);
        var tasks = allUrls.Select(async pair =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await TestSingleProxyAsync(pair.Name, pair.Url);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var taskResults = await Task.WhenAll(tasks);
        results.AddRange(taskResults);

        return results;
    }

    private static async Task<ProxySpeedResult> TestSingleProxyAsync(string name, string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-GitHubRelease");
            var sw = Stopwatch.StartNew();
            using var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, url),
                HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            return new ProxySpeedResult(name, url, sw.Elapsed.TotalMilliseconds,
                response.IsSuccessStatusCode);
        }
        catch
        {
            return new ProxySpeedResult(name, url, double.MaxValue, false);
        }
    }

    public static string GetBestUrl(List<ProxySpeedResult> results, string originalUrl)
    {
        var best = results
            .Where(r => r.Ok)
            .OrderBy(r => r.Ms)
            .FirstOrDefault();

        return best?.Url ?? originalUrl;
    }

    public static string GetCurrentArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };
    }

    public static async Task<string> ShowDownloadFlowAsync(
        BuiltinToolContext context,
        string toolName,
        string description,
        string projectUrl,
        string repo,
        string? tag,
        AssetMatchStrategy strategy,
        string? warningText = null,
        string? sizeHint = null)
    {
        var arch = GetCurrentArch();
        var release = await FetchReleaseByTagAsync(repo, tag);

        if (release is null)
        {
            var errDialog = context.CreateDialog("获取版本信息失败", "确定");
            errDialog.Content = new TextBlock
            {
                Text = "无法从 GitHub 获取版本信息，请检查网络连接后重试。",
                TextWrapping = TextWrapping.Wrap
            };
            await errDialog.ShowAsync();
            return "";
        }

        var asset = FindBestAsset(release.Assets, arch, strategy);
        if (asset is null)
        {
            var errDialog = context.CreateDialog("未找到适配版本", "确定");
            errDialog.Content = new TextBlock
            {
                Text = $"当前架构 {arch} 没有匹配的下载文件。版本：{release.TagName}",
                TextWrapping = TextWrapping.Wrap
            };
            await errDialog.ShowAsync();
            return "";
        }

        var installDialog = context.CreateDialog($"安装{toolName}", "取消");
        installDialog.PrimaryButtonText = "下载安装";
        installDialog.Resources["ContentDialogMaxWidth"] = 540;

        var stack = new StackPanel { Spacing = 12 };

        var descBorder = new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{toolName} v{release.TagName.TrimStart('v')}",
                        FontSize = 15,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = description,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    },
                    new HyperlinkButton
                    {
                        Content = new TextBlock
                        {
                            Text = projectUrl,
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 96, 165, 250))
                        },
                        NavigateUri = new Uri(projectUrl),
                        Padding = new Thickness(0)
                    }
                }
            }
        };
        stack.Children.Add(descBorder);

        if (warningText is not null)
        {
            var warningBorder = new Border
            {
                Padding = new Thickness(14, 10, 14, 10),
                Background = new SolidColorBrush(Color.FromArgb(30, 251, 146, 60)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 251, 146, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\uE7BA",
                            FontSize = 16,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 251, 146, 60))
                        },
                        new TextBlock
                        {
                            Text = warningText,
                            FontSize = 13,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            stack.Children.Add(warningBorder);
        }

        var sizeInfo = new TextBlock
        {
            Text = sizeHint ?? $"文件：{asset.Name}（{ToolDownloaderService.FormatSize(asset.Size)}）· 架构：{arch}",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(sizeInfo);

        installDialog.Content = stack;

        var installResult = await installDialog.ShowAsync();
        if (installResult != ContentDialogResult.Primary)
            return "";

        return await ShowProgressDialogAsync(context, toolName, asset, release.TagName);
    }

    public static async Task<string> ShowProgressDialogAsync(
        BuiltinToolContext context,
        string toolName,
        GitHubAssetInfo asset,
        string versionTag)
    {
        var dialog = context.CreateDialog($"下载 {toolName}", "取消");
        dialog.PrimaryButtonText = "开始下载";
        dialog.IsPrimaryButtonEnabled = false;
        dialog.Resources["ContentDialogMaxWidth"] = 560;

        var fileNameText = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = $"📦 {asset.Name}（{ToolDownloaderService.FormatSize(asset.Size)}）",
            TextWrapping = TextWrapping.Wrap
        };

        var speedStatusIcon = new FontIcon
        {
            Glyph = "\uEC27",
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        };
        var speedStatusText = new TextBlock
        {
            FontSize = 13,
            Text = "正在测速选择最佳下载源...",
            VerticalAlignment = VerticalAlignment.Center
        };
        var speedStatusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { speedStatusIcon, speedStatusText }
        };

        var speedList = new StackPanel { Spacing = 2 };
        var speedScroll = new ScrollViewer
        {
            Content = speedList,
            MaxHeight = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var speedBorder = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 4, 0, 0),
            Child = speedScroll
        };

        var speedPanel = new StackPanel { Spacing = 4 };
        speedPanel.Children.Add(speedStatusRow);
        speedPanel.Children.Add(speedBorder);

        var progressBar = new ProgressBar
        {
            IsIndeterminate = false,
            ShowError = false,
            ShowPaused = false,
            Visibility = Visibility.Collapsed
        };

        var percentText = new TextBlock
        {
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            Text = "0%",
            Visibility = Visibility.Collapsed
        };

        var speedValText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "--"
        };
        var sizeValText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "--"
        };
        var timeValText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "--"
        };

        var statsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            Visibility = Visibility.Collapsed,
            Children =
            {
                new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "速度", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] }, speedValText } },
                new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "已下载", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] }, sizeValText } },
                new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "剩余时间", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] }, timeValText } }
            }
        };

        var progressPanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed
        };
        progressPanel.Children.Add(progressBar);
        progressPanel.Children.Add(percentText);
        progressPanel.Children.Add(statsRow);

        var errorBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = true,
            Severity = InfoBarSeverity.Error,
            Title = "下载失败"
        };

        var contentStack = new StackPanel { Spacing = 12, MinWidth = 440 };
        contentStack.Children.Add(fileNameText);
        contentStack.Children.Add(speedPanel);
        contentStack.Children.Add(progressPanel);
        contentStack.Children.Add(errorBar);
        dialog.Content = contentStack;

        var bestUrl = asset.OriginalUrl;
        var cts = new CancellationTokenSource();
        var downloadStarted = false;

        dialog.CloseButtonClick += (s, e) =>
        {
            if (downloadStarted)
            {
                cts.Cancel();
            }
        };

        _ = Task.Run(async () =>
        {
            var proxyResults = new List<ProxySpeedResult>();
            var semaphore = new SemaphoreSlim(6);
            var testTasks = new List<Task<ProxySpeedResult>>();

            testTasks.Add(TestSingleProxyAsync("GitHub 直连", asset.OriginalUrl));

            foreach (var proxy in GitHubProxies)
            {
                var proxyName = new Uri(proxy).Host;
                var proxyedUrl = ProxyUrl(proxy, asset.OriginalUrl);
                testTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try { return await TestSingleProxyAsync(proxyName, proxyedUrl); }
                    finally { semaphore.Release(); }
                }));
            }

            var remaining = testTasks.ToList();
            var finishedCount = 0;
            var totalCount = remaining.Count;

            while (remaining.Count > 0)
            {
                var finished = await Task.WhenAny(remaining);
                remaining.Remove(finished);
                finishedCount++;

                try
                {
                    var r = await finished;
                    proxyResults.Add(r);

                    dialog.DispatcherQueue.TryEnqueue(() =>
                    {
                        var color = r.Ok
                            ? Color.FromArgb(255, 74, 222, 128)
                            : Color.FromArgb(255, 248, 113, 113);
                        var icon = r.Ok ? "✓" : "✗";
                        var msText = r.Ok ? $"{r.Ms:F0}ms" : "不可用";
                        speedList.Children.Add(new TextBlock
                        {
                            FontSize = 11,
                            Foreground = new SolidColorBrush(color),
                            Text = $"{icon} {r.Name}  —  {msText}"
                        });

                        var okCount = proxyResults.Count(x => x.Ok);
                        speedStatusText.Text = $"正在测速... {finishedCount}/{totalCount}（{okCount} 可用）";
                    });
                }
                catch { }
            }

            var best = proxyResults
                .Where(r => r.Ok)
                .OrderBy(r => r.Ms)
                .FirstOrDefault();

            if (best is not null && best.Url is not null)
                bestUrl = best.Url;

            dialog.DispatcherQueue.TryEnqueue(() =>
            {
                if (best is not null)
                {
                    speedStatusIcon.Glyph = "\uEC61";
                    speedStatusText.Text = $"已选择：{best.Name}（{best.Ms:F0}ms）";
                    speedStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 74, 222, 128));
                }
                else
                {
                    speedStatusIcon.Glyph = "\uE783";
                    speedStatusText.Text = "所有代理不可用，将尝试 GitHub 直连";
                    speedStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 251, 146, 60));
                }
                dialog.IsPrimaryButtonEnabled = true;
            });
        });

        dialog.PrimaryButtonClick += async (s, e) =>
        {
            if (downloadStarted) { e.Cancel = true; return; }
            var deferral = e.GetDeferral();
            e.Cancel = true;

            downloadStarted = true;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.CloseButtonText = "取消下载";

            try
            {
                speedPanel.Visibility = Visibility.Collapsed;
                progressPanel.Visibility = Visibility.Visible;
                progressBar.Visibility = Visibility.Visible;
                percentText.Visibility = Visibility.Visible;
                statsRow.Visibility = Visibility.Visible;
                dialog.PrimaryButtonText = "下载中...";

                var progress = new Progress<ToolDownloadProgress>(p =>
                {
                    dialog.DispatcherQueue.TryEnqueue(() =>
                    {
                        progressBar.Value = p.Percentage;
                        percentText.Text = $"{p.Percentage:F1}%";
                        speedValText.Text = ToolDownloaderService.FormatSpeed(p.SpeedMbps);
                        sizeValText.Text = $"{ToolDownloaderService.FormatSize(p.BytesReceived)} / {ToolDownloaderService.FormatSize(p.TotalBytes)}";
                        timeValText.Text = ToolDownloaderService.FormatTime(p.EstimatedRemaining);
                    });
                });

                var tempDir = Path.Combine(Path.GetTempPath(), $"TubaWinUi3_{toolName.Replace(" ", "_")}");
                var filePath = await ToolDownloaderService.DownloadToFileAsync(
                    bestUrl, tempDir, asset.Name, progress, cts.Token);

                dialog.Hide();

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    var tipDialog = context.CreateDialog("启动安装程序失败", "确定");
                    tipDialog.Content = new TextBlock
                    {
                        Text = $"安装程序已下载到：{filePath}\n\n请手动运行。\n错误：{ex.Message}",
                        TextWrapping = TextWrapping.Wrap
                    };
                    await tipDialog.ShowAsync();
                }
            }
            catch (OperationCanceledException)
            {
                dialog.Hide();
            }
            catch (Exception ex)
            {
                errorBar.Message = ex.Message;
                errorBar.IsOpen = true;
                progressPanel.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.PrimaryButtonText = "重试";
                downloadStarted = false;
            }
            finally
            {
                try { deferral.Complete(); } catch { }
            }
        };

        await dialog.ShowAsync();
        return "";
    }
}

public enum AssetMatchStrategy
{
    Generic,
    UniGetUI,
    OptimizerDuck,
    ContextMenuMgr
}
