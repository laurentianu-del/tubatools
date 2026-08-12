using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed class BrowserPageParam
{
    public required string Url { get; init; }
    public string? Title { get; init; }
}

public sealed partial class BrowserPage : Page
{
    private string _url;

    public BrowserPage()
    {
        InitializeComponent();

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        TitleText.Text = "浏览器";
        _url = "about:blank";
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is BrowserPageParam param)
        {
            _url = param.Url;
            TitleText.Text = param.Title ?? "浏览器";
            _ = InitWebViewAsync();
        }
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());

            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;

            WebView.CoreWebView2.Navigate(_url);
        }
        catch (Exception ex)
        {
            ShowError("WebView2 初始化失败", $"请确保已安装 WebView2 Runtime。\n\n{ex.Message}");
        }
    }

    private void OnNavigationStarting(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
    {
        LoadingRing.IsActive = true;
        ErrorPanel.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Visible;
    }

    private void OnNavigationCompleted(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingRing.IsActive = false;

        if (!args.IsSuccess)
        {
            ShowError("无法加载页面", $"错误：{args.WebErrorStatus}");
        }
    }

    private void OnDocumentTitleChanged(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        object args)
    {
        var docTitle = sender.DocumentTitle;
        if (!string.IsNullOrEmpty(docTitle))
        {
            TitleText.Text = docTitle;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoBack == true)
            WebView.CoreWebView2.GoBack();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.Reload();
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var url = WebView.CoreWebView2?.Source?.ToString() ?? _url;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Visible;
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.Navigate(_url);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateBack();
    }

    private void ShowError(string title, string message)
    {
        WebView.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorTitle.Text = title;
        ErrorMessage.Text = message;
    }

    public static void Open(string url, string? title = null)
    {
        App.MainWindow?.NavigateToToolPage(typeof(BrowserPage), new BrowserPageParam
        {
            Url = url,
            Title = title
        });
    }
}
