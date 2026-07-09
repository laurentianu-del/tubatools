namespace TubaWinUi3.Services;

public static class RuntimeHelper
{
    private static readonly bool _isMsixPackaged = DetectMsixPackaged();
    private static readonly bool _isLiteBuild = DetectLiteBuild();
    private static readonly bool _isInstalled = DetectInstalled();

    public static bool IsMsixPackaged => _isMsixPackaged;

    public static bool IsLiteBuild => _isLiteBuild;

    public static bool IsInstalled => _isInstalled;

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

    private static bool DetectInstalled()
    {
        if (_isMsixPackaged) return true;

        try
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, ".installed");
            return File.Exists(markerPath);
        }
        catch
        {
            return false;
        }
    }
}
