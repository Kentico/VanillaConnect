using System;
using System.Linq;

namespace VanillaConnect
{
    public static class Extensions
	{
		public static string ToHexString(this byte[] buff)
		{
			if (buff == null)
			{
				throw new ArgumentNullException(nameof(buff));
			}
			return buff.Aggregate("", (current, t) => current + t.ToString("x2"));
		}
	}
}
