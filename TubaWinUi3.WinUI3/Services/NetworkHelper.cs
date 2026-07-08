using Windows.Networking.Connectivity;

namespace TubaWinUi3.Services;

public static class NetworkHelper
{
    public static bool IsMeteredConnection()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return true;

            var cost = profile.GetConnectionCost();
            return cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable
                || cost.Roaming || cost.OverDataLimit;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsInternetAvailable()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return profile?.NetworkAdapter is not null;
        }
        catch
        {
            return false;
        }
    }
}
