namespace TubaWinUi3.Services.ViVe;

public struct RTL_FEATURE_CONFIGURATION
{
	public uint FeatureId;

	public uint CompactState;

	public uint VariantPayload;

	public RTL_FEATURE_CONFIGURATION_PRIORITY Priority
	{
		get
		{
			return (RTL_FEATURE_CONFIGURATION_PRIORITY)(CompactState & 0xF);
		}
		set
		{
			if (value > RTL_FEATURE_CONFIGURATION_PRIORITY.ImageOverride)
			{
				throw new FeaturePropertyOverflowException("Priority", 15);
			}
			CompactState = (CompactState & 0xFFFFFFF0u) | (uint)value;
		}
	}

	public RTL_FEATURE_ENABLED_STATE EnabledState
	{
		get
		{
			return (RTL_FEATURE_ENABLED_STATE)((CompactState & 0x30) >> 4);
		}
		set
		{
			if (value > RTL_FEATURE_ENABLED_STATE.Enabled)
			{
				throw new FeaturePropertyOverflowException("EnabledState", 2);
			}
			CompactState = (CompactState & 0xFFFFFFCFu) | ((uint)value << 4);
		}
	}

	public bool IsWexpConfiguration
	{
		get
		{
			return (CompactState & 0x40) >> 6 == 1;
		}
		set
		{
			CompactState = (CompactState & 0xFFFFFFBFu) | ((value ? 1u : 0u) << 6);
		}
	}

	public bool HasSubscriptions
	{
		get
		{
			return (CompactState & 0x80) >> 7 == 1;
		}
		set
		{
			CompactState = (CompactState & 0xFFFFFF7Fu) | ((value ? 1u : 0u) << 7);
		}
	}

	public uint Variant
	{
		get
		{
			return (CompactState & 0x3F00) >> 8;
		}
		set
		{
			if (value > 63)
			{
				throw new FeaturePropertyOverflowException("Variant", 63);
			}
			CompactState = (CompactState & 0xFFFFC0FFu) | (value << 8);
		}
	}

	public RTL_FEATURE_VARIANT_PAYLOAD_KIND VariantPayloadKind
	{
		get
		{
			return (RTL_FEATURE_VARIANT_PAYLOAD_KIND)((CompactState & 0xC000) >> 14);
		}
		set
		{
			if (value > (RTL_FEATURE_VARIANT_PAYLOAD_KIND.Resident | RTL_FEATURE_VARIANT_PAYLOAD_KIND.External))
			{
				throw new FeaturePropertyOverflowException("VariantPayloadKind", 3);
			}
			CompactState = (CompactState & 0xFFFF3FFFu) | ((uint)value << 14);
		}
	}
}
