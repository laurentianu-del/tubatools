using Microsoft.UI.Xaml.Media.Imaging;
using System.Net.Http;
using System.Management;

namespace TubaWinUi3.Services;

public static class BrandEasterEggService
{
    private static readonly HttpClient _http = new();
    private static string? _cachedBrand;
    private static bool _isDownloading;

    private static readonly BrandEasterEgg[] BrandEasterEggs =
    [
        new("ROG", "华硕(ASUS)", "ROG背景.jpg", "https://dlcdnwebimgs.asus.com/dl/f68d6612-ef3c-456f-a8d5-5e91524844a2/"),
        new("Honor", "荣耀", "荣耀背景.webp", "https://assetscdn.c.hihonor.com/myhonor/img/66a31721ef7b7d5e525aed51.webp?fileSize=2000*1000"),
        new("Lenovo", "联想(Lenovo)", "联想背景.jpg", "https://img2cdn.clubstatic.lenovo.com.cn/pic/31917391149167/0"),
        new("MSI", "微星(MSI)", "微星背景.jpeg", "https://storage-asset.msi.com/global/picture/wallpaper/wallpaper_178177079861ba0eff67da563c5cd38b5730760761.jpeg"),
        new("ASRock", "华擎(ASRock)", "华擎背景.jpg", "https://download.asrock.com/Wallpaper/2024_Taichi-3440x1440.jpg"),
        new("Gigabyte", "技嘉(Gigabyte)", "技嘉背景.jpg", "https://images7.alphacoders.com/119/thumb-1920-1191487.jpg"),
    ];

    public static event EventHandler<BrandEasterEggLoadedEventArgs>? BrandBackgroundLoaded;

    public static string? GetDetectedBrand() => _cachedBrand ??= DetectBrand();

    public static BrandEasterEgg? GetDetectedEasterEgg()
    {
        var brand = GetDetectedBrand();
        if (brand is null) return null;
        return BrandEasterEggs.FirstOrDefault(e => brand.Contains(e.MatchKeyword, StringComparison.OrdinalIgnoreCase));
    }

    public static string? GetBrandBackgroundPath()
    {
        var easterEgg = GetDetectedEasterEgg();
        if (easterEgg is null) return null;

        var dir = ConfigManager.GetBackgroundsDir();
        var path = Path.Combine(dir, easterEgg.FileName);
        return File.Exists(path) ? path : null;
    }

    public static void StartBackgroundDownload()
    {
        if (_isDownloading) return;

        var easterEgg = GetDetectedEasterEgg();
        if (easterEgg is null) return;

        var dir = ConfigManager.GetBackgroundsDir();
        var path = Path.Combine(dir, easterEgg.FileName);

        if (File.Exists(path)) return;

        _isDownloading = true;

        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(dir);

                using var response = await _http.GetAsync(easterEgg.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var tmpPath = path + ".tmp";
                using (var fs = File.Create(tmpPath))
                {
                    await response.Content.CopyToAsync(fs);
                }

                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmpPath, path);

                BrandBackgroundLoaded?.Invoke(null, new BrandEasterEggLoadedEventArgs(easterEgg.Name, path));
            }
            catch { }
            finally
            {
                _isDownloading = false;
            }
        });
    }

    public static bool ShouldApplyBrandBackground()
    {
        var customBg = BackgroundService.GetBackgroundPath();
        return string.IsNullOrEmpty(customBg) && GetDetectedBrand() is not null;
    }

    public static void ApplyBrandBackgroundIfDetected()
    {
        if (!ShouldApplyBrandBackground()) return;

        var path = GetBrandBackgroundPath();
        if (File.Exists(path))
        {
            BackgroundService.SetBackgroundPath(path);
        }
    }

    private static string? DetectBrand()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
            foreach (ManagementBaseObject board in searcher.Get())
            {
                var mfr = board["Manufacturer"]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(mfr)) continue;

                var cleaned = CleanBoardManufacturer(mfr);
                if (!string.IsNullOrEmpty(cleaned))
                    return cleaned;
            }
        }
        catch { }

        return null;
    }

    private static string? CleanBoardManufacturer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var upper = raw.Trim().ToUpperInvariant();

        if (upper.Contains("ASUS") || upper.Contains("ASUSTEK")) return "华硕(ASUS)";
        if (upper.Contains("MSI") || upper.Contains("MICRO-STAR")) return "微星(MSI)";
        if (upper.Contains("GIGABYTE")) return "技嘉(Gigabyte)";
        if (upper.Contains("ASROCK")) return "华擎(ASRock)";
        if (upper.Contains("HUAWEI") || upper.Contains("HONOR")) return "荣耀";
        if (upper.Contains("LENOVO")) return "联想(Lenovo)";

        return null;
    }
}

public sealed class BrandEasterEgg
{
    public string Name { get; }
    public string MatchKeyword { get; }
    public string FileName { get; }
    public string DownloadUrl { get; }

    public BrandEasterEgg(string name, string matchKeyword, string fileName, string downloadUrl)
    {
        Name = name;
        MatchKeyword = matchKeyword;
        FileName = fileName;
        DownloadUrl = downloadUrl;
    }
}

public sealed class BrandEasterEggLoadedEventArgs : EventArgs
{
    public string BrandName { get; }
    public string ImagePath { get; }

    public BrandEasterEggLoadedEventArgs(string brandName, string imagePath)
    {
        BrandName = brandName;
        ImagePath = imagePath;
    }
}