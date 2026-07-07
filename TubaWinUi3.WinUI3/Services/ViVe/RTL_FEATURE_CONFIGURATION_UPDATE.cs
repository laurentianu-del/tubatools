namespace TubaWinUi3.Services.ViVe;

public struct RTL_FEATURE_CONFIGURATION_UPDATE
{
	public uint FeatureId;

	private RTL_FEATURE_CONFIGURATION_PRIORITY _priority;

	private RTL_FEATURE_ENABLED_STATE _enabledState;

	private RTL_FEATURE_ENABLED_STATE_OPTIONS _enabledStateOptions;

	private uint _variant;

	private RTL_FEATURE_VARIANT_PAYLOAD_KIND _variantPayloadKind;

	public uint VariantPayload;

	private RTL_FEATURE_CONFIGURATION_OPERATION _operation;

	public RTL_FEATURE_CONFIGURATION_PRIORITY Priority
	{
		get
		{
			return _priority;
		}
		set
		{
			if (value > RTL_FEATURE_CONFIGURATION_PRIORITY.ImageOverride)
			{
				throw new FeaturePropertyOverflowException("Priority", 15);
			}
			_priority = value;
		}
	}

	public RTL_FEATURE_ENABLED_STATE EnabledState
	{
		get
		{
			return _enabledState;
		}
		set
		{
			if (value > RTL_FEATURE_ENABLED_STATE.Enabled)
			{
				throw new FeaturePropertyOverflowException("EnabledState", 2);
			}
			_enabledState = value;
		}
	}

	public RTL_FEATURE_ENABLED_STATE_OPTIONS EnabledStateOptions
	{
		get
		{
			return _enabledStateOptions;
		}
		set
		{
			if ((uint)value > 1u)
			{
				throw new FeaturePropertyOverflowException("EnabledStateOptions", 1);
			}
			_enabledStateOptions = value;
		}
	}

	public uint Variant
	{
		get
		{
			return _variant;
		}
		set
		{
			if (value > 63)
			{
				throw new FeaturePropertyOverflowException("Variant", 63);
			}
			_variant = value;
		}
	}

	public RTL_FEATURE_VARIANT_PAYLOAD_KIND VariantPayloadKind
	{
		get
		{
			return _variantPayloadKind;
		}
		set
		{
			if (value > (RTL_FEATURE_VARIANT_PAYLOAD_KIND.Resident | RTL_FEATURE_VARIANT_PAYLOAD_KIND.External))
			{
				throw new FeaturePropertyOverflowException("VariantPayloadKind", 3);
			}
			_variantPayloadKind = value;
		}
	}

	public RTL_FEATURE_CONFIGURATION_OPERATION Operation
	{
		get
		{
			return _operation;
		}
		set
		{
			if (value > RTL_FEATURE_CONFIGURATION_OPERATION.ResetState)
			{
				throw new FeaturePropertyOverflowException("Operation", 4);
			}
			_operation = value;
		}
	}

	public bool UserPolicyPriorityCompatible
	{
		get
		{
			if (EnabledStateOptions == RTL_FEATURE_ENABLED_STATE_OPTIONS.None && _variant == 0 && _variantPayloadKind == RTL_FEATURE_VARIANT_PAYLOAD_KIND.None)
			{
				return VariantPayload == 0;
			}
			return false;
		}
	}
}
