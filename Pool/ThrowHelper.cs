using System;

namespace Pool
{
	public static class ThrowHelper
	{
		public static void ThrowIfNull<T>(this T obj, string paramName) where T : class
		{
			if (obj == null)
			{
				throw new ArgumentNullException(paramName);
			}
		}

		public static void ThrowIfOutOfRange(this int value, int min, int max, string paramName)
		{
			if (value < min || value >= max)
			{
				throw new ArgumentOutOfRangeException(paramName, $"Value should be in range {min} to {max - 1}.");
			}
		}
	
		public static void ThrowIfNegative(this int value, string paramName)
		{
			if (value < 0)
			{
				throw new ArgumentOutOfRangeException(paramName, "Value should be non-negative.");
			}
		}
	}
}
