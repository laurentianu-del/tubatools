using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.ViVe;

public static class NtdllInterop
{
	[DllImport("ntdll.dll")]
	public unsafe static extern int RtlQueryAllFeatureConfigurations(RTL_FEATURE_CONFIGURATION_TYPE featureConfigurationType, ulong* changeStamp, RTL_FEATURE_CONFIGURATION* featureConfigurations, out int featureConfigurationCount);

	[DllImport("ntdll.dll")]
	public static extern int RtlQueryFeatureConfiguration(uint featureId, RTL_FEATURE_CONFIGURATION_TYPE featureConfigurationType, ref ulong changeStamp, out RTL_FEATURE_CONFIGURATION featureConfiguration);

	[DllImport("ntdll.dll")]
	public static extern ulong RtlQueryFeatureConfigurationChangeStamp();

	[DllImport("ntdll.dll")]
	public unsafe static extern int RtlQueryFeatureUsageNotificationSubscriptions(RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS* subscriptions, out int subscriptionCount);

	[DllImport("ntdll.dll")]
	public static extern int RtlSetFeatureConfigurations(ref ulong previousChangeStamp, RTL_FEATURE_CONFIGURATION_TYPE featureConfigurationType, RTL_FEATURE_CONFIGURATION_UPDATE[] featureConfigurations, int featureConfigurationCount);

	[DllImport("ntdll.dll")]
	public static extern int RtlRegisterFeatureConfigurationChangeNotification(FeatureConfigurationChangeCallback callback, nint context, nint waitForChangeStamp, out nint subscription);

	[DllImport("ntdll.dll")]
	public static extern int RtlRegisterFeatureConfigurationChangeNotification(FeatureConfigurationChangeCallback callback, nint context, ref ulong waitForChangeStamp, out nint subscription);

	[DllImport("ntdll.dll")]
	public static extern int RtlUnregisterFeatureConfigurationChangeNotification(nint subscription);

	[DllImport("ntdll.dll")]
	public static extern int RtlSubscribeForFeatureUsageNotification(RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[] subscriptions, int subscriptionCount);

	[DllImport("ntdll.dll")]
	public static extern int RtlUnsubscribeFromFeatureUsageNotifications(RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[] subscriptions, int subscriptionCount);

	[DllImport("ntdll.dll")]
	public static extern int RtlNotifyFeatureUsage(ref RTL_FEATURE_USAGE_REPORT report);

	[DllImport("ntdll.dll")]
	public static extern int RtlSetSystemBootStatus(int bsdItemType, ref int data, int dataLength, nint returnLength);

	[DllImport("ntdll.dll")]
	public static extern int RtlGetSystemBootStatus(int bsdItemType, out int data, int dataLength, nint returnLength);

	[DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
	public static extern int RtlCreateBootStatusDataFile(string bootStatusPath);

	[DllImport("ntdll.dll")]
	public static extern int RtlNtStatusToDosError(int ntStatus);
}
