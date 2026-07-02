using System;

namespace TubaWinUi3.Services.ViVe;

[Flags]
public enum RTL_FEATURE_VARIANT_PAYLOAD_KIND : uint
{
	None = 0u,
	Resident = 1u,
	External = 2u
}
