using System.Collections.Generic;
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
		public static string GetString(HttpContext context, string name, string defaultValue = "")
		{
			string raw = context.Request.Query[name].FirstOrDefault();
			if (string.IsNullOrEmpty(raw))
			{
				return defaultValue;
			}
			return raw;
		}

		/// <summary>Returns true when the named param is "1" or "true" (case-insensitive).</summary>
		public static bool GetBool(HttpContext context, string name, bool defaultValue = false)
		{
			string raw = context.Request.Query[name].FirstOrDefault();
			if (string.IsNullOrEmpty(raw))
			{
				return defaultValue;
			}
			return raw == "1" || string.Equals(raw, "true", System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Returns all values for a multi-valued query param (e.g. repeated ?songId=1&amp;songId=2).</summary>
		public static List<string> GetList(HttpContext context, string name)
		{
			return context.Request.Query[name].ToList();
		}

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
		public static string GetUserId(HttpContext context)
		{
			// Identity is the modern `uid=` only. The legacy `u=` username bridge
			// was removed once every client moved to uid.
			return GetString(context, "uid", "");
		}
	}
}
