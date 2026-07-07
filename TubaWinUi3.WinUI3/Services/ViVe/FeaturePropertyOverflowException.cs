using System;

namespace TubaWinUi3.Services.ViVe;

public class FeaturePropertyOverflowException : Exception
{
	public FeaturePropertyOverflowException(string propertyName, int maximumValue)
		: base($"{propertyName} must not be higher than {maximumValue}.")
	{
	}
}
