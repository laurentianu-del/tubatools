namespace TubaWinUi3.Services.ViVe;

public static class ObfuscationHelpers
{
	private static uint SwapBytes(uint x)
	{
		x = (x >> 16) | (x << 16);
		return ((x & 0xFF00FF00u) >> 8) | ((x & 0xFF00FF) << 8);
	}

	private static uint RotateRight32(uint value, int shift)
	{
		return (value >> shift) | (value << 32 - shift);
	}

	public static uint ObfuscateFeatureId(uint featureId)
	{
		return RotateRight32(SwapBytes(featureId ^ 0x74161A4E) ^ 0x8FB23D4Fu, -1) ^ 0x833EA8FFu;
	}

	public static uint DeobfuscateFeatureId(uint featureId)
	{
		return SwapBytes(RotateRight32(featureId ^ 0x833EA8FFu, 1) ^ 0x8FB23D4Fu) ^ 0x74161A4E;
	}
}
