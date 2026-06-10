namespace TubaWinUi3.Services;

public static class RuntimeHelper
{
    private static readonly bool _isMsixPackaged = DetectMsixPackaged();

    public static bool IsMsixPackaged => _isMsixPackaged;

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
}
