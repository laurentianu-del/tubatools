using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TubaWinUi3.Services.ViVe;

public static class FeatureManager
{
	public static readonly RTL_FEATURE_CONFIGURATION_PRIORITY[] ImmutablePriorities = new RTL_FEATURE_CONFIGURATION_PRIORITY[5]
	{
		RTL_FEATURE_CONFIGURATION_PRIORITY.ImageDefault,
		RTL_FEATURE_CONFIGURATION_PRIORITY.EKB,
		RTL_FEATURE_CONFIGURATION_PRIORITY.ImageDefaultEditionOverride,
		RTL_FEATURE_CONFIGURATION_PRIORITY.Security,
		RTL_FEATURE_CONFIGURATION_PRIORITY.ImageOverride
	};

	private const int RtlBsdItemFeatureConfigurationState = 17;

	public unsafe static RTL_FEATURE_CONFIGURATION[]? QueryAllFeatureConfigurations(RTL_FEATURE_CONFIGURATION_TYPE configurationType = RTL_FEATURE_CONFIGURATION_TYPE.Runtime)
	{
		ulong num = 0uL;
		return QueryAllFeatureConfigurations(configurationType, &num);
	}

	public unsafe static RTL_FEATURE_CONFIGURATION[]? QueryAllFeatureConfigurations(RTL_FEATURE_CONFIGURATION_TYPE configurationType, ulong* changeStamp)
	{
		int retries = 3;
		while (retries-- > 0)
		{
			NtdllInterop.RtlQueryAllFeatureConfigurations(configurationType, changeStamp, null, out var featureConfigurationCount);
			if (featureConfigurationCount == 0)
			{
				return null;
			}
			int allocatedCount = featureConfigurationCount + 64;
			RTL_FEATURE_CONFIGURATION[] array = new RTL_FEATURE_CONFIGURATION[allocatedCount];
			fixed (RTL_FEATURE_CONFIGURATION* featureConfigurations = array)
			{
				int status = NtdllInterop.RtlQueryAllFeatureConfigurations(configurationType, changeStamp, featureConfigurations, out var actualCount);
				if (status == 0)
				{
					if (actualCount <= allocatedCount)
					{
						if (actualCount < array.Length)
						{
							Array.Resize(ref array, actualCount);
						}
						return array;
					}
					continue;
				}
				return null;
			}
		}
		return null;
	}

	public static RTL_FEATURE_CONFIGURATION? QueryFeatureConfiguration(uint featureId, RTL_FEATURE_CONFIGURATION_TYPE configurationType = RTL_FEATURE_CONFIGURATION_TYPE.Runtime)
	{
		ulong changeStamp = 0uL;
		return QueryFeatureConfiguration(featureId, configurationType, ref changeStamp);
	}

	public static RTL_FEATURE_CONFIGURATION? QueryFeatureConfiguration(uint featureId, RTL_FEATURE_CONFIGURATION_TYPE configurationType, ref ulong changeStamp)
	{
		if (NtdllInterop.RtlQueryFeatureConfiguration(featureId, configurationType, ref changeStamp, out var featureConfiguration) != 0)
		{
			return null;
		}
		return featureConfiguration;
	}

	public static ulong QueryFeatureConfigurationChangeStamp()
	{
		return NtdllInterop.RtlQueryFeatureConfigurationChangeStamp();
	}

	public static int SetFeatureConfigurations(RTL_FEATURE_CONFIGURATION_UPDATE[] updates, RTL_FEATURE_CONFIGURATION_TYPE configurationType = RTL_FEATURE_CONFIGURATION_TYPE.Runtime)
	{
		ulong previousChangeStamp = 0uL;
		return SetFeatureConfigurations(updates, configurationType, ref previousChangeStamp);
	}

	public static int SetFeatureConfigurations(RTL_FEATURE_CONFIGURATION_UPDATE[] updates, RTL_FEATURE_CONFIGURATION_TYPE configurationType, ref ulong previousChangeStamp)
	{
		for (int i = 0; i < updates.Length; i++)
		{
			RTL_FEATURE_CONFIGURATION_UPDATE rTL_FEATURE_CONFIGURATION_UPDATE = updates[i];
			if (ImmutablePriorities.Contains(rTL_FEATURE_CONFIGURATION_UPDATE.Priority, null))
			{
				throw new ArgumentException($"{rTL_FEATURE_CONFIGURATION_UPDATE.Priority} ({(int)rTL_FEATURE_CONFIGURATION_UPDATE.Priority}) is an immutable priority and can't be written to.");
			}
			if (rTL_FEATURE_CONFIGURATION_UPDATE.Priority == RTL_FEATURE_CONFIGURATION_PRIORITY.UserPolicy && !rTL_FEATURE_CONFIGURATION_UPDATE.UserPolicyPriorityCompatible)
			{
				throw new ArgumentException("UserPolicy priority overrides do not support persisting properties other than EnabledState.");
			}
		}
		if (configurationType == RTL_FEATURE_CONFIGURATION_TYPE.Runtime)
		{
			return NtdllInterop.RtlSetFeatureConfigurations(ref previousChangeStamp, RTL_FEATURE_CONFIGURATION_TYPE.Runtime, updates, updates.Length);
		}
		return SetFeatureConfigurationsInRegistry(updates, previousChangeStamp);
	}

	public unsafe static RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[]? QueryFeatureUsageSubscriptions()
	{
		NtdllInterop.RtlQueryFeatureUsageNotificationSubscriptions(null, out var subscriptionCount);
		if (subscriptionCount == 0)
		{
			return null;
		}
		RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[] array = new RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[subscriptionCount];
		fixed (RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS* subscriptions = array)
		{
			if (NtdllInterop.RtlQueryFeatureUsageNotificationSubscriptions(subscriptions, out subscriptionCount) == 0)
			{
				return array;
			}
		}
		return null;
	}

	public static int AddFeatureUsageSubscriptions(RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[] subscriptions)
	{
		return NtdllInterop.RtlSubscribeForFeatureUsageNotification(subscriptions, subscriptions.Length);
	}

	public static int RemoveFeatureUsageSubscriptions(RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[] subscriptions)
	{
		return NtdllInterop.RtlUnsubscribeFromFeatureUsageNotifications(subscriptions, subscriptions.Length);
	}

	public static int NotifyFeatureUsage(ref RTL_FEATURE_USAGE_REPORT report)
	{
		return NtdllInterop.RtlNotifyFeatureUsage(ref report);
	}

	public static int SetBootFeatureConfigurationState(BSD_FEATURE_CONFIGURATION_STATE state)
	{
		int data = (int)state;
		return NtdllInterop.RtlSetSystemBootStatus(17, ref data, 4, IntPtr.Zero);
	}

	public static int GetBootFeatureConfigurationState(out BSD_FEATURE_CONFIGURATION_STATE state)
	{
		int data;
		int result = NtdllInterop.RtlGetSystemBootStatus(17, out data, 4, IntPtr.Zero);
		state = (BSD_FEATURE_CONFIGURATION_STATE)data;
		return result;
	}

	public static bool FixLKGStore()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("CurrentControlSet\\Control\\FeatureManagement\\LastKnownGood", writable: true);
			if (registryKey == null)
			{
				return false;
			}
			byte[] array = (byte[])registryKey.GetValue("LKGConfiguration");
			if (array == null)
			{
				return false;
			}
			if (BitConverter.ToInt32(array, 0) == 0)
			{
				return false;
			}
			int num = Marshal.SizeOf(typeof(RTL_FEATURE_CONFIGURATION));
			byte[] array2 = new byte[array.Length - num];
			Array.Copy(array, 4 + num, array2, 4, array2.Length - 4);
			registryKey.SetValue("LKGConfiguration", array2);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static int InitializeBootStatusDataFile()
	{
		return NtdllInterop.RtlCreateBootStatusDataFile(null);
	}

	private static int SetFeatureConfigurationsInRegistry(RTL_FEATURE_CONFIGURATION_UPDATE[] updates, ulong previousStamp)
	{
		if (previousStamp != 0)
		{
			ulong num = QueryFeatureConfigurationChangeStamp();
			if (previousStamp != num)
			{
				return -1073741823;
			}
		}
		try
		{
			for (int i = 0; i < updates.Length; i++)
			{
				RTL_FEATURE_CONFIGURATION_UPDATE rTL_FEATURE_CONFIGURATION_UPDATE = updates[i];
				bool flag = rTL_FEATURE_CONFIGURATION_UPDATE.Priority == RTL_FEATURE_CONFIGURATION_PRIORITY.UserPolicy;
				string text = ObfuscationHelpers.ObfuscateFeatureId(rTL_FEATURE_CONFIGURATION_UPDATE.FeatureId).ToString();
				string text2 = (flag ? "SYSTEM\\CurrentControlSet\\Policies\\Microsoft\\FeatureManagement\\Overrides" : $"SYSTEM\\CurrentControlSet\\Control\\FeatureManagement\\Overrides\\{(int)rTL_FEATURE_CONFIGURATION_UPDATE.Priority}\\{text}");
				if (rTL_FEATURE_CONFIGURATION_UPDATE.Operation == RTL_FEATURE_CONFIGURATION_OPERATION.ResetState)
				{
					if (flag)
					{
						RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(text2, writable: true);
						if (registryKey != null)
						{
							using (registryKey)
							{
								registryKey.DeleteValue(text, throwOnMissingValue: false);
							}
						}
					}
					else
					{
						Registry.LocalMachine.DeleteSubKeyTree(text2, throwOnMissingSubKey: false);
					}
					continue;
				}
				using RegistryKey registryKey2 = Registry.LocalMachine.CreateSubKey(text2);
				if (rTL_FEATURE_CONFIGURATION_UPDATE.Operation.HasFlag(RTL_FEATURE_CONFIGURATION_OPERATION.FeatureState))
				{
					if (flag)
					{
						registryKey2.SetValue(text, (int)rTL_FEATURE_CONFIGURATION_UPDATE.EnabledState);
					}
					else
					{
						registryKey2.SetValue("EnabledState", (int)rTL_FEATURE_CONFIGURATION_UPDATE.EnabledState);
						registryKey2.SetValue("EnabledStateOptions", (int)rTL_FEATURE_CONFIGURATION_UPDATE.EnabledStateOptions);
					}
				}
				if (!flag && rTL_FEATURE_CONFIGURATION_UPDATE.Operation.HasFlag(RTL_FEATURE_CONFIGURATION_OPERATION.VariantState))
				{
					registryKey2.SetValue("Variant", (int)rTL_FEATURE_CONFIGURATION_UPDATE.Variant);
					registryKey2.SetValue("VariantPayload", (int)rTL_FEATURE_CONFIGURATION_UPDATE.VariantPayload);
					registryKey2.SetValue("VariantPayloadKind", (int)rTL_FEATURE_CONFIGURATION_UPDATE.VariantPayloadKind);
				}
			}
			return 0;
		}
		catch (Exception ex)
		{
			return ex.HResult;
		}
	}

	public static int AddFeatureUsageSubscriptionsToRegistry(RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[] subscriptions)
	{
		try
		{
			for (int i = 0; i < subscriptions.Length; i++)
			{
				RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS = subscriptions[i];
				uint value = ObfuscationHelpers.ObfuscateFeatureId(rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS.FeatureId);
				using RegistryKey registryKey = Registry.LocalMachine.CreateSubKey($"SYSTEM\\CurrentControlSet\\Control\\FeatureManagement\\UsageSubscriptions\\{value}\\{{{Guid.NewGuid()}}}");
				registryKey.SetValue("ReportingKind", (int)rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS.ReportingKind);
				registryKey.SetValue("ReportingOptions", (int)rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS.ReportingOptions);
				registryKey.SetValue("ReportingTarget", BitConverter.GetBytes(rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS.ReportingTarget));
			}
			return 0;
		}
		catch (Exception ex)
		{
			return ex.HResult;
		}
	}

	public static int RemoveFeatureUsageSubscriptionsFromRegistry(RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS[] subscriptions)
	{
		try
		{
			for (int i = 0; i < subscriptions.Length; i++)
			{
				RTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS = subscriptions[i];
				string text = "SYSTEM\\CurrentControlSet\\Control\\FeatureManagement\\UsageSubscriptions\\" + ObfuscationHelpers.ObfuscateFeatureId(rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS.FeatureId);
				RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(text, writable: true);
				if (registryKey == null)
				{
					continue;
				}
				using (registryKey)
				{
					string[] subKeyNames = registryKey.GetSubKeyNames();
					foreach (string text2 in subKeyNames)
					{
						bool flag = false;
						using (RegistryKey registryKey2 = registryKey.OpenSubKey(text2))
						{
							if (registryKey2 != null && (int)registryKey2.GetValue("ReportingKind") == rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS.ReportingKind && BitConverter.ToUInt64((byte[])registryKey2.GetValue("ReportingTarget"), 0) == rTL_FEATURE_USAGE_SUBSCRIPTION_DETAILS.ReportingTarget)
							{
								flag = true;
							}
						}
						if (flag)
						{
							registryKey.DeleteSubKeyTree(text2, throwOnMissingSubKey: false);
						}
					}
					if (registryKey.SubKeyCount == 0)
					{
						Registry.LocalMachine.DeleteSubKeyTree(text, throwOnMissingSubKey: false);
					}
				}
			}
			return 0;
		}
		catch (Exception ex)
		{
			return ex.HResult;
		}
	}

	public static string GetHumanErrorDescription(int ntStatus, bool noTranslate = false)
	{
		int num = 0;
		if (!noTranslate)
		{
			num = NtdllInterop.RtlNtStatusToDosError(ntStatus);
		}
		if (noTranslate || num == 317)
		{
			num = ntStatus;
		}
		return new Win32Exception(num).Message;
	}
}
