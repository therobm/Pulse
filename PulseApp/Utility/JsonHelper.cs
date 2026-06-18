using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace PulseApp.Utility
{
	public static  class JsonHelper
	{
		
		public static string GetString(JsonElement element, string property)
		{
			if (element.TryGetProperty(property, out JsonElement value))
			{
				if (value.ValueKind == JsonValueKind.String)
				{
					return value.GetString();
				}
				if (value.ValueKind == JsonValueKind.Number)
				{
					return value.ToString();
				}
			}
			return null;
		}

		public static int GetInt(JsonElement element, string property)
		{
			if (element.TryGetProperty(property, out JsonElement value))
			{
				if (value.ValueKind == JsonValueKind.Number)
				{
					return value.GetInt32();
				}
			}
			return 0;
		}

		public static float GetFloat(JsonElement element, string property)
		{
			if (element.TryGetProperty(property, out JsonElement value))
			{
				if (value.ValueKind == JsonValueKind.Number)
				{
					return value.GetSingle();
				}
			}
			return 0f;
		}

		public static DateTime GetDateTime(JsonElement element, string property)
		{
			if (element.TryGetProperty(property, out JsonElement value))
			{
				if (value.ValueKind == JsonValueKind.String)
				{
					string raw = value.GetString();
					if (!string.IsNullOrEmpty(raw))
					{
						DateTime parsed;
						if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
						{
							return parsed;
						}
					}
				}
			}
			return DateTime.MinValue;
		}
	}
}
