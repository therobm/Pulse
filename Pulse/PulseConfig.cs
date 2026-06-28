using PulseIngestion;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pulse
{
	public class PulseConfig
	{
		public int HttpPort { get; set; } = 32457;
		public int HttpsPort { get; set; } = 32458;

		public string AudiobooksPath { get; set; } = "";
		public string PodcastPath = "";
		public string MusicPath { get; set; } = "";


		public string PulseDataPath = "";
		public string SpotifyClient = "";
		public string SpotifySecret = "";
		public string SpotifyRedirectURI = "";

		public string LidarrURL { get; set; } = "";
		public string LidarrApiKey { get; set; } = "";

		public string HttpsCertPath { get; set; } = "";
		public string DatabaseEnvironment { get; set; } = "Production";
	
		public string PodcastSearchUrl { get; set; } = "https://itunes.apple.com/search?term={query}&entity=podcast";

		public bool EnforceModernApi = false;

		public int LibraryScanInterval = 30; //30 min default

		public IngestionConfig IngestionConfiguration = new IngestionConfig();



		public static string GetConfigPath()
		{
			string exePath = System.Environment.ProcessPath;
			string exeDir = Path.GetDirectoryName(exePath);
			return Path.Combine(exeDir, "pulse.config.json");
		}

		public List<string> Validate()
		{
			List<string> errors = new List<string>();

			if (string.IsNullOrWhiteSpace(MusicPath))
			{
				errors.Add("MusicPath is required");
			}
			else if (!Directory.Exists(MusicPath))
			{
				errors.Add("MusicPath '" + MusicPath + "' does not exist");
			}

			if (string.IsNullOrWhiteSpace(PodcastPath))
			{
				errors.Add("PodcastPath is required");
			}
			else if (!Directory.Exists(MusicPath))
			{
				errors.Add("PodcastPath '" + PodcastPath + "' does not exist");
			}

			if (string.IsNullOrWhiteSpace(PulseDataPath))
			{
				errors.Add("PulseDataPath is required");
			}
			else if (!Directory.Exists(PulseDataPath))
			{
				errors.Add("PulseDataPath '" + PulseDataPath + "' does not exist");
			}

			if (HttpPort <= 0 || HttpPort > 65535)
			{
				errors.Add("HttpPort " + HttpPort.ToString() + " is not a valid port (1-65535)");
			}

			if (HttpsPort <= 0 || HttpsPort > 65535)
			{
				errors.Add("HttpsPort " + HttpsPort.ToString() + " is not a valid port (1-65535)");
			}

			return errors;
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
				Log.Warning("Pulse: wrote blank config to " + path + " - fill it in and restart.");
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
