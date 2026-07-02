using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using TubaWinUi3.Services.ViVe;

namespace TubaWinUi3.Services;

public static class ViVeService
{
	public static bool IsSupported => Environment.OSVersion.Version.Build >= 18963;

	public static List<ViVeFeatureEntry> QueryFeatures(ViVeStoreType store)
	{
		var configs = FeatureManager.QueryAllFeatureConfigurations(store != ViVeStoreType.Boot ? RTL_FEATURE_CONFIGURATION_TYPE.Runtime : RTL_FEATURE_CONFIGURATION_TYPE.Boot);
		if (configs == null || configs.Length == 0)
		{
			return [];
		}
		var names = FeatureNaming.FindNamesForFeatures(configs.Select(x => x.FeatureId));
		var list = new List<ViVeFeatureEntry>(configs.Length);
		foreach (var cfg in configs)
		{
			string name = null;
			names?.TryGetValue(cfg.FeatureId, out name);
			list.Add(new ViVeFeatureEntry
			{
				FeatureId = cfg.FeatureId,
				Name = name,
				Priority = cfg.Priority,
				EnabledState = cfg.EnabledState,
				IsWexpConfiguration = cfg.IsWexpConfiguration,
				HasSubscriptions = cfg.HasSubscriptions,
				Variant = cfg.Variant,
				VariantPayloadKind = cfg.VariantPayloadKind,
				VariantPayload = cfg.VariantPayload,
				Store = store
			});
		}
		return list;
	}

	public static ViVeFeatureEntry? QueryFeature(uint featureId, ViVeStoreType store)
	{
		var configurationType = store != ViVeStoreType.Boot ? RTL_FEATURE_CONFIGURATION_TYPE.Runtime : RTL_FEATURE_CONFIGURATION_TYPE.Boot;
		var cfgNullable = FeatureManager.QueryFeatureConfiguration(featureId, configurationType);
		if (!cfgNullable.HasValue)
		{
			return null;
		}
		var names = FeatureNaming.FindNamesForFeatures(new[] { featureId });
		string name = null;
		names?.TryGetValue(featureId, out name);
		var cfg = cfgNullable.Value;
		return new ViVeFeatureEntry
		{
			FeatureId = cfg.FeatureId,
			Name = name,
			Priority = cfg.Priority,
			EnabledState = cfg.EnabledState,
			IsWexpConfiguration = cfg.IsWexpConfiguration,
			HasSubscriptions = cfg.HasSubscriptions,
			Variant = cfg.Variant,
			VariantPayloadKind = cfg.VariantPayloadKind,
			VariantPayload = cfg.VariantPayload,
			Store = store
		};
	}

	public static ViVeResult EnableFeature(uint featureId, ViVeStoreType store, RTL_FEATURE_CONFIGURATION_PRIORITY priority = RTL_FEATURE_CONFIGURATION_PRIORITY.User)
	{
		return SetFeatureState(featureId, store, RTL_FEATURE_ENABLED_STATE.Enabled, priority);
	}

	public static ViVeResult DisableFeature(uint featureId, ViVeStoreType store, RTL_FEATURE_CONFIGURATION_PRIORITY priority = RTL_FEATURE_CONFIGURATION_PRIORITY.User)
	{
		return SetFeatureState(featureId, store, RTL_FEATURE_ENABLED_STATE.Disabled, priority);
	}

	public static ViVeResult ResetFeature(uint featureId, ViVeStoreType store, RTL_FEATURE_CONFIGURATION_PRIORITY? priority = null)
	{
		try
		{
			var configurationType = store != ViVeStoreType.Boot ? RTL_FEATURE_CONFIGURATION_TYPE.Runtime : RTL_FEATURE_CONFIGURATION_TYPE.Boot;
			List<RTL_FEATURE_CONFIGURATION_UPDATE> updates;
			if (priority.HasValue)
			{
				int count = 1;
				var list = new List<RTL_FEATURE_CONFIGURATION_UPDATE>(count);
				CollectionsMarshal.SetCount(list, count);
				CollectionsMarshal.AsSpan(list)[0] = new RTL_FEATURE_CONFIGURATION_UPDATE
				{
					FeatureId = featureId,
					Priority = priority.Value,
					Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
				};
				updates = list;
			}
			else
			{
				var allConfigs = FeatureManager.QueryAllFeatureConfigurations(configurationType);
				if (allConfigs == null)
				{
					return ViVeResult.Fail("未找到该功能的配置");
				}
				updates = [];
				foreach (var cfg in allConfigs)
				{
					if (cfg.FeatureId == featureId && !FeatureManager.ImmutablePriorities.Contains(cfg.Priority, null))
					{
						updates.Add(new RTL_FEATURE_CONFIGURATION_UPDATE
						{
							FeatureId = cfg.FeatureId,
							Priority = cfg.Priority,
							Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
						});
					}
				}
				if (updates.Count == 0)
				{
					return ViVeResult.Fail("未找到可重置的配置");
				}
			}
			int status = FeatureManager.SetFeatureConfigurations(updates.ToArray(), configurationType);
			if (status != 0)
			{
				return ViVeResult.Fail(FeatureManager.GetHumanErrorDescription(status));
			}
			if (configurationType == RTL_FEATURE_CONFIGURATION_TYPE.Boot)
			{
				UpdateLKGStatus(BSD_FEATURE_CONFIGURATION_STATE.BootPending);
			}
			return ViVeResult.Ok();
		}
		catch (Exception ex)
		{
			return ViVeResult.Fail(ex.Message);
		}
	}

	public static ViVeResult FullReset(ViVeStoreType store)
	{
		try
		{
			var configurationType = store != ViVeStoreType.Boot ? RTL_FEATURE_CONFIGURATION_TYPE.Runtime : RTL_FEATURE_CONFIGURATION_TYPE.Boot;
			var allConfigs = FeatureManager.QueryAllFeatureConfigurations(configurationType);
			if (allConfigs == null || allConfigs.Length == 0)
			{
				return ViVeResult.Ok();
			}
			var updates = new List<RTL_FEATURE_CONFIGURATION_UPDATE>();
			foreach (var cfg in allConfigs)
			{
				if (!FeatureManager.ImmutablePriorities.Contains(cfg.Priority, null))
				{
					updates.Add(new RTL_FEATURE_CONFIGURATION_UPDATE
					{
						FeatureId = cfg.FeatureId,
						Priority = cfg.Priority,
						Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
					});
				}
			}
			if (updates.Count == 0)
			{
				return ViVeResult.Ok();
			}
			int status = FeatureManager.SetFeatureConfigurations(updates.ToArray(), configurationType);
			if (status != 0)
			{
				return ViVeResult.Fail(FeatureManager.GetHumanErrorDescription(status));
			}
			if (configurationType == RTL_FEATURE_CONFIGURATION_TYPE.Boot)
			{
				UpdateLKGStatus(BSD_FEATURE_CONFIGURATION_STATE.BootPending);
			}
			return ViVeResult.Ok();
		}
		catch (Exception ex)
		{
			return ViVeResult.Fail(ex.Message);
		}
	}

	public static ulong QueryChangeStamp()
	{
		return FeatureManager.QueryFeatureConfigurationChangeStamp();
	}

	public static ViVeBootState QueryBootState()
	{
		int result = FeatureManager.GetBootFeatureConfigurationState(out var state);
		return new ViVeBootState
		{
			Success = result == 0,
			State = state,
			ErrorMessage = result != 0 ? FeatureManager.GetHumanErrorDescription(result) : null
		};
	}

	public static ViVeResult FixLKG()
	{
		if (!FeatureManager.FixLKGStore())
		{
			return ViVeResult.Fail("LKG 存储无需修复或修复失败");
		}
		return ViVeResult.Ok();
	}

	public static ViVeResult FixPriority(ViVeStoreType store)
	{
		try
		{
			var configurationType = store != ViVeStoreType.Boot ? RTL_FEATURE_CONFIGURATION_TYPE.Runtime : RTL_FEATURE_CONFIGURATION_TYPE.Boot;
			var allConfigs = FeatureManager.QueryAllFeatureConfigurations(configurationType);
			if (allConfigs == null)
			{
				return ViVeResult.Ok();
			}
			var serviceConfigs = allConfigs.Where(x => x.Priority == RTL_FEATURE_CONFIGURATION_PRIORITY.Service && !x.IsWexpConfiguration).ToList();
			if (serviceConfigs.Count == 0)
			{
				return ViVeResult.Ok();
			}
			var updates = new RTL_FEATURE_CONFIGURATION_UPDATE[serviceConfigs.Count * 2];
			int idx = 0;
			foreach (var cfg in serviceConfigs)
			{
				updates[idx++] = new RTL_FEATURE_CONFIGURATION_UPDATE
				{
					FeatureId = cfg.FeatureId,
					Priority = cfg.Priority,
					Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
				};
				updates[idx++] = new RTL_FEATURE_CONFIGURATION_UPDATE
				{
					FeatureId = cfg.FeatureId,
					Priority = RTL_FEATURE_CONFIGURATION_PRIORITY.User,
					EnabledState = cfg.EnabledState,
					Variant = cfg.Variant,
					VariantPayloadKind = cfg.VariantPayloadKind,
					VariantPayload = cfg.VariantPayload,
					Operation = RTL_FEATURE_CONFIGURATION_OPERATION.FeatureState | RTL_FEATURE_CONFIGURATION_OPERATION.VariantState
				};
			}
			int status = FeatureManager.SetFeatureConfigurations(updates, configurationType);
			if (status != 0)
			{
				return ViVeResult.Fail(FeatureManager.GetHumanErrorDescription(status));
			}
			return ViVeResult.Ok();
		}
		catch (Exception ex)
		{
			return ViVeResult.Fail(ex.Message);
		}
	}

	public static ViVeResult ExportFeatures(string filePath, ViVeStoreType store)
	{
		try
		{
			var configurations = FeatureManager.QueryAllFeatureConfigurations(store != ViVeStoreType.Boot ? RTL_FEATURE_CONFIGURATION_TYPE.Runtime : RTL_FEATURE_CONFIGURATION_TYPE.Boot);
			using var output = new FileStream(filePath, FileMode.Create);
			using var bw = new BinaryWriter(output);
			SerializeConfigsToStream(bw, configurations);
			return ViVeResult.Ok();
		}
		catch (Exception ex)
		{
			return ViVeResult.Fail(ex.Message);
		}
	}

	public static ViVeResult ExportAllFeatures(string filePath)
	{
		try
		{
			var runtimeConfigs = FeatureManager.QueryAllFeatureConfigurations();
			var bootConfigs = FeatureManager.QueryAllFeatureConfigurations(RTL_FEATURE_CONFIGURATION_TYPE.Boot);
			using var output = new FileStream(filePath, FileMode.Create);
			using var bw = new BinaryWriter(output);
			SerializeConfigsToStream(bw, runtimeConfigs);
			SerializeConfigsToStream(bw, bootConfigs);
			return ViVeResult.Ok();
		}
		catch (Exception ex)
		{
			return ViVeResult.Fail(ex.Message);
		}
	}

	public static ViVeImportResult ImportFeatures(string filePath, bool replaceExisting)
	{
		try
		{
			List<RTL_FEATURE_CONFIGURATION> runtimeConfigs;
			List<RTL_FEATURE_CONFIGURATION> bootConfigs;
			using (var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				using var br = new BinaryReader(input);
				runtimeConfigs = DeserializeConfigsFromStream(br);
				bootConfigs = DeserializeConfigsFromStream(br);
			}
			if (replaceExisting)
			{
				FullReset(ViVeStoreType.Runtime);
				FullReset(ViVeStoreType.Boot);
			}
			if (runtimeConfigs.Count > 0)
			{
				int status = FeatureManager.SetFeatureConfigurations(ConvertConfigsToUpdates(runtimeConfigs));
				if (status != 0)
				{
					return new ViVeImportResult
					{
						Success = false,
						ErrorMessage = FeatureManager.GetHumanErrorDescription(status)
					};
				}
			}
			if (bootConfigs.Count > 0)
			{
				int status = FeatureManager.SetFeatureConfigurations(ConvertConfigsToUpdates(bootConfigs), RTL_FEATURE_CONFIGURATION_TYPE.Boot);
				if (status != 0)
				{
					return new ViVeImportResult
					{
						Success = false,
						ErrorMessage = FeatureManager.GetHumanErrorDescription(status)
					};
				}
				UpdateLKGStatus(BSD_FEATURE_CONFIGURATION_STATE.BootPending);
			}
			return new ViVeImportResult
			{
				Success = true,
				RuntimeCount = runtimeConfigs.Count,
				BootCount = bootConfigs.Count
			};
		}
		catch (Exception ex)
		{
			return new ViVeImportResult
			{
				Success = false,
				ErrorMessage = ex.Message
			};
		}
	}

	public static List<uint>? SearchFeatureIdsByName(string name)
	{
		return FeatureNaming.FindIdsForNames(new[] { name });
	}

	private static ViVeResult SetFeatureState(uint featureId, ViVeStoreType store, RTL_FEATURE_ENABLED_STATE state, RTL_FEATURE_CONFIGURATION_PRIORITY priority)
	{
		try
		{
			var configurationType = store != ViVeStoreType.Boot ? RTL_FEATURE_CONFIGURATION_TYPE.Runtime : RTL_FEATURE_CONFIGURATION_TYPE.Boot;
			var update = new RTL_FEATURE_CONFIGURATION_UPDATE
			{
				FeatureId = featureId,
				EnabledState = state,
				Priority = priority,
				Operation = RTL_FEATURE_CONFIGURATION_OPERATION.FeatureState | RTL_FEATURE_CONFIGURATION_OPERATION.VariantState
			};
			int status = FeatureManager.SetFeatureConfigurations([update], configurationType);
			if (status != 0)
			{
				return ViVeResult.Fail(FeatureManager.GetHumanErrorDescription(status));
			}
			if (configurationType == RTL_FEATURE_CONFIGURATION_TYPE.Boot)
			{
				UpdateLKGStatus(BSD_FEATURE_CONFIGURATION_STATE.BootPending);
			}
			return ViVeResult.Ok();
		}
		catch (Exception ex)
		{
			return ViVeResult.Fail(ex.Message);
		}
	}

	private static void UpdateLKGStatus(BSD_FEATURE_CONFIGURATION_STATE newStatus)
	{
		switch (FeatureManager.GetBootFeatureConfigurationState(out var state))
		{
		case -1073741772:
			if (FeatureManager.InitializeBootStatusDataFile() != 0)
			{
				return;
			}
			break;
		default:
			return;
		case 0:
			break;
		}
		if (state != newStatus)
		{
			FeatureManager.SetBootFeatureConfigurationState(newStatus);
		}
	}

	private static void SerializeConfigsToStream(BinaryWriter bw, RTL_FEATURE_CONFIGURATION[]? configurations)
	{
		if (configurations != null)
		{
			bw.Write(configurations.Length);
			for (int i = 0; i < configurations.Length; i++)
			{
				var cfg = configurations[i];
				bw.Write(cfg.FeatureId);
				bw.Write(cfg.CompactState);
				bw.Write(cfg.VariantPayload);
			}
		}
		else
		{
			bw.Write(0);
		}
	}

	private static List<RTL_FEATURE_CONFIGURATION> DeserializeConfigsFromStream(BinaryReader br)
	{
		int count = br.ReadInt32();
		var list = new List<RTL_FEATURE_CONFIGURATION>();
		for (int i = 0; i < count; i++)
		{
			var cfg = new RTL_FEATURE_CONFIGURATION
			{
				FeatureId = br.ReadUInt32(),
				CompactState = br.ReadUInt32(),
				VariantPayload = br.ReadUInt32()
			};
			if (!FeatureManager.ImmutablePriorities.Contains(cfg.Priority, null))
			{
				list.Add(cfg);
			}
		}
		return list;
	}

	private static RTL_FEATURE_CONFIGURATION_UPDATE[] ConvertConfigsToUpdates(List<RTL_FEATURE_CONFIGURATION> configurations)
	{
		var updates = new RTL_FEATURE_CONFIGURATION_UPDATE[configurations.Count];
		for (int i = 0; i < updates.Length; i++)
		{
			var cfg = configurations[i];
			updates[i] = new RTL_FEATURE_CONFIGURATION_UPDATE
			{
				FeatureId = cfg.FeatureId,
				Priority = cfg.Priority,
				EnabledState = cfg.EnabledState,
				EnabledStateOptions = cfg.IsWexpConfiguration ? RTL_FEATURE_ENABLED_STATE_OPTIONS.WexpConfig : RTL_FEATURE_ENABLED_STATE_OPTIONS.None,
				Variant = cfg.Variant,
				VariantPayloadKind = cfg.VariantPayloadKind,
				VariantPayload = cfg.VariantPayload,
				Operation = RTL_FEATURE_CONFIGURATION_OPERATION.FeatureState | RTL_FEATURE_CONFIGURATION_OPERATION.VariantState
			};
		}
		return updates;
	}
}
