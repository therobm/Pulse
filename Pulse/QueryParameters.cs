using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Pulse
{
	// Shared helpers for reading typed values off the request query string.
	public static class QueryParameters
	{
		// Returns the named integer query param, or defaultValue when the param
		// is missing OR present-but-non-numeric (Flatline #307). int.Parse threw
		// a FormatException on bad client input, surfacing as a 500.
		public static int GetInt(HttpContext context, string name, int defaultValue)
		{
			string raw = context.Request.Query[name].FirstOrDefault();
			int value;
			if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out value))
			{
				return defaultValue;
			}
			return value;
		}

		// Same contract as GetInt: missing or unparseable -> defaultValue.
		public static bool GetBool(HttpContext context, string name, bool defaultValue)
		{
			string raw = context.Request.Query[name].FirstOrDefault();
			bool value;
			if (string.IsNullOrEmpty(raw) || !bool.TryParse(raw, out value))
			{
				return defaultValue;
			}
			return value;
		}
	}
}
