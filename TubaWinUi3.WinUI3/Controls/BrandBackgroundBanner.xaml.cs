using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

public sealed partial class BrandBackgroundBanner : UserControl
{
    private string? _brandName;

    public BrandBackgroundBanner()
    {
        InitializeComponent();
    }

    public void Show(string brandName)
    {
        _brandName = brandName;
        BannerText.Text = $"{brandName}专属壁纸已经下载完毕，如果不需要请点击「不需要」";
        Visibility = Visibility.Visible;
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        BackgroundService.SetBackgroundPath(null);
        BrandEasterEggService.SetBrandBackgroundDisabled(true);
        Visibility = Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
    }
}
