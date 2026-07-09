namespace TubaWinUi3.Services;

public static class RuntimeHelper
{
    private static readonly bool _isMsixPackaged = DetectMsixPackaged();
    private static readonly bool _isLiteBuild = DetectLiteBuild();
    private static readonly bool _isInnoSetupInstalled = DetectInnoSetupInstalled();

    public static bool IsMsixPackaged => _isMsixPackaged;

    public static bool IsLiteBuild => _isLiteBuild;

    public static bool IsInnoSetupInstalled => _isInnoSetupInstalled;

    public static bool IsInstalled => _isMsixPackaged || _isInnoSetupInstalled;

    private static bool DetectMsixPackaged()
    {
        try
        {
            var _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectLiteBuild()
    {
        if (_isMsixPackaged) return false;

        try
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, ".lite_build");
            return File.Exists(markerPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectInnoSetupInstalled()
    {
        if (_isMsixPackaged) return false;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DA3D64F4-winui3-Tuba-2025}_is1");
            if (key is not null)
            {
                var installLocation = key.GetValue("InstallLocation") as string
                    ?? key.GetValue("Inno Setup: App Path") as string;
                if (!string.IsNullOrEmpty(installLocation))
                {
                    var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var regDir = installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return string.Equals(appDir, regDir, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch { }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{DA3D64F4-winui3-Tuba-2025}_is1");
            if (key is not null)
            {
                var installLocation = key.GetValue("InstallLocation") as string
                    ?? key.GetValue("Inno Setup: App Path") as string;
                if (!string.IsNullOrEmpty(installLocation))
                {
                    var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var regDir = installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return string.Equals(appDir, regDir, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch { }

        return false;
    }
}
