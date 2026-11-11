using Windows.Networking.Connectivity;

namespace MyTelU_Launcher.Services;

internal static class InternetConnection
{
    public static bool IsAvailable =>
        NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel()
        == NetworkConnectivityLevel.InternetAccess;
}
