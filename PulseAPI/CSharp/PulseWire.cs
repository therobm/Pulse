using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseAPI.CSharp
{

	/// <summary>
	/// The single wire codec for the Pulse API. The server writes objects to a
	/// response through Serialize; clients read a response back through Parse.
	/// Both sides share these options so the wire format has exactly one
	/// definition. The object field names ARE the wire names -- no name
	/// transformation is applied.
	/// </summary>
	public static class PulseWire
	{
		static JsonSerializerOptions s_options = BuildOptions();

		static JsonSerializerOptions BuildOptions()
		{
			JsonSerializerOptions options = new JsonSerializerOptions();
			options.IncludeFields = true;
			options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
			options.Converters.Add(new JsonStringEnumConverter());
			return options;
		}

		public static string Serialize(object value)
		{
			return JsonSerializer.Serialize(value, s_options);
		}

		public static T Parse<T>(string json)
		{
			return JsonSerializer.Deserialize<T>(json, s_options);
		}
	}
}
