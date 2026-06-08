using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pulse
{
	public class PulseConfig
	{
		public int HttpPort { get; set; } = 32457;
		public int HttpsPort { get; set; } = 32458;

		public string PodcastPath = "";
		public string PulseDataPath = "";
		public string MusicPath { get; set; } = "";
		public string SpotifyClient = "";
		public string SpotifySecret = "";
		public string SpotifyRedirectURI = "";

		public string LidarrURL { get; set; } = "";
		public string LidarrApiKey { get; set; } = "";

		public string HttpsCertPath { get; set; } = "";

		// "Production" (default) or "Staging". Picks which sqlite DB file under
		// PulseData/ is used. Replaces the old Debugger.IsAttached coupling.
		public string DatabaseEnvironment { get; set; } = "Production";

		// Podcast discovery service. The server owns podcast search and proxies
		// to whatever service this URL points at; swapping providers is a config
		// change, not a code change. "{query}" is replaced with the URL-encoded
		// search term. Default is Apple's keyless iTunes Search API.
		public string PodcastSearchUrl { get; set; } = "https://itunes.apple.com/search?term={query}&entity=podcast";

		// Root folder scanned for audiobooks. Each folder that directly contains
		// audio files is one audiobook; each file is one chapter (a single file is
		// a one-chapter book). Empty disables audiobook scanning.
		public string AudiobooksPath { get; set; } = "";


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
				Log.Warning(-1, "Pulse: wrote blank config to " + path + " - fill it in and restart.");
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
