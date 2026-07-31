using Microsoft.UI.Xaml.Media.Imaging;
using System.Net.Http;
using System.Management;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

public static class BrandEasterEggService
{
    private static readonly HttpClient _http = new();
    private static string? _cachedBrand;
    private static bool _downloadAttempted;

    private const string DisableBrandBackgroundKey = "DisableBrandBackground";

    private static readonly BrandEasterEgg[] BrandEasterEggs =
    [
        new("ROG", "华硕(ASUS)", "ROG背景.jpg", "https://dlcdnwebimgs.asus.com/dl/f68d6612-ef3c-456f-a8d5-5e91524844a2/"),
        new("Honor", "荣耀", "荣耀背景.webp", "https://assetscdn.c.hihonor.com/myhonor/img/66a317216a3f6e472f4e560f.webp?fileSize=2000*1000"),
        new("Lenovo", "联想(Lenovo)", "联想背景.jpg", "https://img2cdn.clubstatic.lenovo.com.cn/pic/31917391149167/0"),
        new("MSI", "微星(MSI)", "微星背景.jpeg", "https://storage-asset.msi.com/global/picture/wallpaper/wallpaper_178177079861ba0eff67da563c5cd38b5730760761.jpeg"),
        new("ASRock", "华擎(ASRock)", "华擎背景.jpg", "https://download.asrock.com/Wallpaper/2024_Taichi-3440x1440.jpg"),
        new("Gigabyte", "技嘉(Gigabyte)", "技嘉背景.jpg", "https://images7.alphacoders.com/119/thumb-1920-1191487.jpg"),
    ];

    public static event EventHandler<BrandEasterEggLoadedEventArgs>? BrandBackgroundLoaded;

    public static string? GetDetectedBrand() => _cachedBrand;

    public static async Task<string?> DetectBrandAsync()
    {
        if (_cachedBrand is not null) return _cachedBrand;
        _cachedBrand = await Task.Run(DetectBrand);
        return _cachedBrand;
    }

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

    public static async Task StartBackgroundDownloadAsync()
    {
        if (_downloadAttempted) return;
        if (IsBrandBackgroundDisabled()) return;

        var easterEgg = await GetDetectedEasterEggAsync();
        if (easterEgg is null) return;

        var dir = ConfigManager.GetBackgroundsDir();
        var path = Path.Combine(dir, easterEgg.FileName);

        if (File.Exists(path)) return;

        _downloadAttempted = true;

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
        });
    }

    public static void StartBackgroundDownload()
    {
        _ = StartBackgroundDownloadAsync();
    }

    public static async Task<BrandEasterEgg?> GetDetectedEasterEggAsync()
    {
        var brand = await DetectBrandAsync();
        if (brand is null) return null;
        return BrandEasterEggs.FirstOrDefault(e => brand.Contains(e.MatchKeyword, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ShouldApplyBrandBackground()
    {
        if (IsBrandBackgroundDisabled()) return false;
        var customBg = BackgroundService.GetBackgroundPath();
        return string.IsNullOrEmpty(customBg) && GetDetectedBrand() is not null;
    }

    public static async Task ApplyBrandBackgroundIfDetectedAsync()
    {
        var brand = await DetectBrandAsync();
        if (brand is null) return;
        if (!ShouldApplyBrandBackground()) return;

        var path = GetBrandBackgroundPath();
        if (File.Exists(path))
        {
            BackgroundService.SetBackgroundPath(path);
        }
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

    public static bool IsBrandBackgroundDisabled()
    {
        return AppSettings.GetBool(DisableBrandBackgroundKey, false);
    }

    public static void SetBrandBackgroundDisabled(bool disabled)
    {
        AppSettings.Set(DisableBrandBackgroundKey, disabled);
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

    private static readonly (string pattern, string replacement)[] NicknameRules =
    [
        (@"华硕\(ASUS\)", "打人硕"),
        (@"ASUS|ASUSTEK", "打人硕"),
        (@"微星\(MSI\)", "军规星"),
        (@"MSI|MICRO[\s\-]?STAR", "军规星"),
        (@"技嘉\(Gigabyte\)", "拒保嘉"),
        (@"GIGABYTE", "拒保嘉"),
        (@"华擎\(ASRock\)", "妖板擎"),
        (@"ASROCK", "妖板擎"),
        (@"七彩虹\(Colorful\)", "凄惨红"),
        (@"COLORFUL", "凄惨红"),
        (@"铭瑄\(Maxsun\)", "丐帮瑄"),
        (@"MAXSUN", "丐帮瑄"),
        (@"盈通\(Yeston\)", "花姑娘通"),
        (@"YESTON", "花姑娘通"),
        (@"影驰\(Galax\)", "花驰"),
        (@"GALAX|GALAXY", "花驰"),
        (@"映泰\(Biostar\)", "映泰(不泰)"),
        (@"BIOSTAR", "映泰(不泰)"),
        (@"梅捷\(Soyo\)", "没捷"),
        (@"SOYO", "没捷"),
        (@"昂达\(Onda\)", "昂达(不达)"),
        (@"ONDA", "昂达(不达)"),
        (@"富士康\(Foxconn\)", "血汗工厂康"),
        (@"FOXCONN", "血汗工厂康"),
        (@"英特尔\(Intel\)", "牙膏厂"),
        (@"INTEL", "牙膏厂"),
        (@"超微\(Supermicro\)", "超微(不微)"),
        (@"SUPERMICRO", "超微(不微)"),
        (@"戴尔\(Dell\)", "人傻钱多戴"),
        (@"DELL", "人傻钱多戴"),
        (@"惠普\(HP\)", "铁板烧普"),
        (@"\bHP\b", "铁板烧普"),
        (@"联想\(Lenovo\)", "美帝良心想"),
        (@"LENOVO", "美帝良心想"),
        (@"宏碁\(Acer\)", "宏碁(不碁)"),
        (@"\bACER\b", "宏碁(不碁)"),
        (@"三星\(Samsung\)", "星巴克"),
        (@"SAMSUNG", "星巴克"),
        (@"苹果\(Apple\)", "水果厂"),
        (@"\bAPPLE\b", "水果厂"),
        (@"华为\(Huawei\)", "菊花厂"),
        (@"HUAWEI", "菊花厂"),
        (@"小米\(Xiaomi\)", "粗粮厂"),
        (@"XIAOMI", "粗粮厂"),
        (@"荣耀", "不知道什么耀"),
        (@"HONOR", "不知道什么耀"),
        (@"金士顿\(Kingston\)", "金士顿(假士顿)"),
        (@"KINGSTON", "金士顿(假士顿)"),
        (@"海盗船\(Corsair\)", "贼船"),
        (@"CORSAIR", "贼船"),
        (@"英睿达\(Crucial\)", "英睿达(不达)"),
        (@"CRUCIAL", "英睿达(不达)"),
        (@"海力士\(SK Hynix\)", "海力士(不力)"),
        (@"HYNIX|SK\s*HYNIX", "海力士(不力)"),
        (@"美光\(Micron\)", "美光(不光)"),
        (@"MICRON", "美光(不光)"),
        (@"威刚\(ADATA\)", "威刚(不刚)"),
        (@"\bADATA\b", "威刚(不刚)"),
        (@"芝奇\(G\.Skill\)", "芝奇(不奇)"),
        (@"G[\.\s]?SKILL", "芝奇(不奇)"),
        (@"十铨\(TeamGroup\)", "十铨(不铨)"),
        (@"TEAM\s*GROUP", "十铨(不铨)"),
        (@"\bEVGA\b", "EVGay"),
        (@"\bNZXT\b", "恩杰(不杰)"),
        (@"京东方\(BOE\)", "京东方(不方)"),
        (@"\bBOE\b", "京东方(不方)"),
        (@"友达\(AU Optronics\)", "友达(不达)"),
        (@"AU\s*OPTRONICS", "友达(不达)"),
        (@"飞利浦\(Philips\)", "飞利浦(不浦)"),
        (@"PHILIPS", "飞利浦(不浦)"),
        (@"优派\(ViewSonic\)", "优派(不派)"),
        (@"VIEWSONIC", "优派(不派)"),
        (@"夏普\(Sharp\)", "夏普(不普)"),
        (@"\bSHARP\b", "夏普(不普)"),
        (@"东芝\(Toshiba\)", "东芝(不芝)"),
        (@"TOSHIBA", "东芝(不芝)"),
        (@"索尼\(Sony\)", "大法"),
        (@"\bSONY\b", "大法"),
        (@"\bAMD\b", "农企"),
        (@"NVIDIA|GEFORCE|RTX|GTX", "老黄家"),
        (@"RADEON", "农企"),
        (@"QUALCOMM", "高通(不高)"),
        (@"SNAPDRAGON", "火龙"),
        (@"ADRENO", "阿德瑞诺"),
    ];

    public static string ApplyNickname(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        foreach (var (pattern, replacement) in NicknameRules)
        {
            try
            {
                if (Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase))
                    value = Regex.Replace(value, pattern, replacement, RegexOptions.IgnoreCase);
            }
            catch { }
        }

        return value;
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