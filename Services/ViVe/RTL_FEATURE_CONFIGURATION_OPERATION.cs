using System;

namespace TubaWinUi3.Services.ViVe;

[Flags]
public enum RTL_FEATURE_CONFIGURATION_OPERATION : uint
{
	None = 0u,
	FeatureState = 1u,
	VariantState = 2u,
	ResetState = 4u
}
