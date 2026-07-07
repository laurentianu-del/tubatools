using TubaWinUi3.Services.ViVe;

namespace TubaWinUi3.Services;

public sealed class ViVeFeatureEntry
{
	public uint FeatureId { get; init; }
	public string? Name { get; init; }
	public RTL_FEATURE_CONFIGURATION_PRIORITY Priority { get; init; }
	public RTL_FEATURE_ENABLED_STATE EnabledState { get; init; }
	public bool IsWexpConfiguration { get; init; }
	public bool HasSubscriptions { get; init; }
	public uint Variant { get; init; }
	public RTL_FEATURE_VARIANT_PAYLOAD_KIND VariantPayloadKind { get; init; }
	public uint VariantPayload { get; init; }
	public ViVeStoreType Store { get; init; }

	public string DisplayName => Name ?? FeatureId.ToString();

	public string StateLabel => EnabledState switch
	{
		RTL_FEATURE_ENABLED_STATE.Enabled => "已启用",
		RTL_FEATURE_ENABLED_STATE.Disabled => "已禁用",
		_ => "默认",
	};

	public string PriorityLabel => Priority.ToString();
}
