using System.IO;
using System.Text.Json;

namespace Pulse
{
	public class PulseConfig
	{
		public int HttpPort { get; set; } = 32457;
		public int HttpsPort { get; set; } = 32458;

		public string MusicPath { get; set; } = "";
		public string SpotifyClient = "";
		public string SpotifySecret = "";

		public string LidarrURL { get; set; } = "";
		public string LidarrApiKey { get; set; } = "";


		public static string GetConfigPath()
		{
			return Path.Combine(System.AppContext.BaseDirectory, "pulse.config.json");
		}

		public static PulseConfig Load()
		{
			string path = GetConfigPath();
			if (!File.Exists(path))
			{
				PulseConfig empty = new PulseConfig();
				JsonSerializerOptions writeOptions = new JsonSerializerOptions();
				writeOptions.IncludeFields = true;
				writeOptions.WriteIndented = true;
				File.WriteAllText(path, JsonSerializer.Serialize(empty, writeOptions));
				System.Console.WriteLine("Pulse: wrote blank config to " + path + " - fill it in and restart.");
				return empty;
			}

			string json = File.ReadAllText(path);
			JsonSerializerOptions readOptions = new JsonSerializerOptions();
			readOptions.IncludeFields = true;
			PulseConfig config = JsonSerializer.Deserialize<PulseConfig>(json, readOptions);
			if (config == null)
			{
				return new PulseConfig();
			}
			return config;
		}
	}
}
