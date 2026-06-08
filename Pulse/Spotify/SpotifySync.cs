using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Pulse.MusicLibrary;

namespace Pulse.Spotify
{
	public class SpotifySync
	{
		private string m_clientId;
		private string m_clientSecret;
		private string m_redirectUri;
		private string m_accessToken;
		private string m_refreshToken;
		private DateTime m_tokenExpiry;
		private string m_credentialPath;
		private MusicManager m_musicManager;
		private HttpClient m_httpClient;
		private Thread m_syncThread;
		private bool m_running;
		private int m_syncIntervalHours;
		private string m_userName;
		public bool IsAuthorized()
		{
			return !string.IsNullOrEmpty(m_refreshToken);
		}
		public bool IsRunning()
		{
			return m_running;
		}

		public SpotifySync(string userName, MusicManager musicManager, string clientId, string clientSecret, string redirectUri, string credentialPath)
		{
			m_userName = userName;
			m_musicManager = musicManager;
			m_clientId = clientId;
			m_clientSecret = clientSecret;
			m_redirectUri = redirectUri;
			m_credentialPath = credentialPath;
			m_accessToken = "";
			m_refreshToken = "";
			m_tokenExpiry = DateTime.MinValue;
			m_syncIntervalHours = 12;
			m_httpClient = new HttpClient();
			m_httpClient.Timeout = TimeSpan.FromSeconds(10);
			LoadCredentials();
		}

		/// <summary>
		/// Returns the Spotify authorization URL. Shannon opens this in a browser once.
		/// </summary>
		public string GetAuthorizationUrl(string state)
		{
			string scopes = "playlist-read-private playlist-read-collaborative";
			string url = "https://accounts.spotify.com/authorize"
				+ "?client_id=" + Uri.EscapeDataString(m_clientId)
				+ "&response_type=code"
				+ "&redirect_uri=" + Uri.EscapeDataString(m_redirectUri)
				+ "&scope=" + Uri.EscapeDataString(scopes)
				+ "&state=" + Uri.EscapeDataString(state);
			return url;
		}

		/// <summary>
		/// Called by your HTTP server when Spotify redirects back with the authorization code.
		/// </summary>
		public bool HandleCallback(string code)
		{
			string body = "grant_type=authorization_code"
				+ "&code=" + Uri.EscapeDataString(code)
				+ "&redirect_uri=" + Uri.EscapeDataString(m_redirectUri);

			using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token"))
			{
				request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");

				string authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes(m_clientId + ":" + m_clientSecret));
				request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

				using (HttpResponseMessage response = m_httpClient.Send(request))
				{
					if (!response.IsSuccessStatusCode)
					{
						string errorBody = ReadResponseBody(response);
						Log.Error(-1, "Spotify: Token exchange failed - " + response.StatusCode + " - " + errorBody);
						return false;
					}

					string json = ReadResponseBody(response);
					JsonDocument doc = JsonDocument.Parse(json);
					JsonElement root = doc.RootElement;

					m_accessToken = root.GetProperty("access_token").GetString();
					m_refreshToken = root.GetProperty("refresh_token").GetString();
					int expiresIn = root.GetProperty("expires_in").GetInt32();
					m_tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

					SaveCredentials();
					Log.Info(-1, "Spotify: Authorization complete");
					return true;
				}
			}
		}

		/// <summary>
		/// Starts the background sync loop.
		/// </summary>
		public void Start()
		{
			if (m_running)
			{
				return;
			}
			if (!IsAuthorized())
			{
				//https://pulse.mccoder.com:32458/spotify/authorize/yourNameHere
				Log.Warning(-1, "Spotify: Not authorized. Visit: " + GetAuthorizationUrl("yourNameHere"));
				return;
			}

			m_running = true;
			m_syncThread = new Thread(SyncLoop);
			m_syncThread.IsBackground = true;
			m_syncThread.Name = "Pulse.SpotifySync";
			m_syncThread.Start();
		}

		public void Stop()
		{
			m_running = false;
		}

		private void SyncLoop()
		{
			Log.Info(-1, "Spotify: Sync loop started, interval " + m_syncIntervalHours + " hours");
			for (;;)
			{
				if (!m_running)
				{
					break;
				}
				try
				{
					SyncAllPlaylists();
				}
				catch (Exception ex)
				{
					Log.Error(-1, "Spotify: Sync error - " + ex.Message + "\n" + ex.StackTrace);
				}

				for (int waited = 0; waited < m_syncIntervalHours * 60 * 60 && m_running; waited++)
				{
					Thread.Sleep(1000);
				}
			}
			Log.Info(-1, "Spotify: Sync loop stopped");
		}

		private void SyncAllPlaylists()
		{
			Log.Info(-1, "Spotify: SyncAllPlaylists v2");
			if (!EnsureToken())
			{
				Log.Error(-1, "Spotify: Failed to refresh token");
				return;
			}

			List<SpotifyPlaylistEntry> playlists = FetchUserPlaylists();
			Log.Info(-1, "Spotify: Found " + playlists.Count + " playlists");
			foreach (SpotifyPlaylistEntry entry in playlists)
			{
				List<PlaylistImportEntry> tracks = FetchPlaylistTracks(entry.Id);
				if (tracks.Count == 0)
				{
					Log.Warning(-1, "Importing playlist: No Tracks found in list!");
					continue;
				}
				m_musicManager.ImportPlaylist(entry.Name, tracks);
			}
			m_musicManager.OnPlaylistSyncComplete();
		}

		private List<SpotifyPlaylistEntry> FetchUserPlaylists()
		{
			List<SpotifyPlaylistEntry> playlists = new List<SpotifyPlaylistEntry>();
			string url = "https://api.spotify.com/v1/me/playlists?limit=50&offset=0";
			for (;;)
			{
				if (string.IsNullOrEmpty(url))
				{
					break;
				}
				string json = SpotifyGet(url);
				if (json == null)
				{
					break;
				}
				JsonDocument doc = JsonDocument.Parse(json);
				JsonElement root = doc.RootElement;
				JsonElement items = root.GetProperty("items");
				for (int index = 0; index < items.GetArrayLength(); index++)
				{
					JsonElement item = items[index];
					SpotifyPlaylistEntry entry = new SpotifyPlaylistEntry();
					entry.Id = item.GetProperty("id").GetString();
					entry.Name = item.GetProperty("name").GetString();
					// Property name "items" is intentional and verified against the actual Spotify response we receive. Do not change without re-checking the live response shape.
					JsonElement tracksObj = item.GetProperty("items");
					entry.TrackCount = tracksObj.GetProperty("total").GetInt32();
					playlists.Add(entry);
				}
				JsonElement nextElement;
				if (root.TryGetProperty("next", out nextElement) && nextElement.ValueKind != JsonValueKind.Null)
				{
					url = nextElement.GetString();
				}
				else
				{
					url = null;
				}
				doc.Dispose();
				Thread.Sleep(200);
			}
			return playlists;
		}

		private List<PlaylistImportEntry> FetchPlaylistTracks(string playlistId)
		{			
			List<PlaylistImportEntry> tracks = new List<PlaylistImportEntry>();
			string url = "https://api.spotify.com/v1/playlists/" + playlistId + "/items?limit=50&offset=0";
			for (;;)
			{
				if (string.IsNullOrEmpty(url))
				{
					break;
				}
				string json = SpotifyGet(url);
				if (json == null)
				{
					break;
				}
				JsonDocument doc = JsonDocument.Parse(json);
				JsonElement root = doc.RootElement;
				JsonElement items = root.GetProperty("items");
				for (int index = 0; index < items.GetArrayLength(); index++)
				{
					JsonElement item = items[index];
					JsonElement trackElement;
					// Property name "item" is intentional and verified against the actual Spotify response we receive. Do not change without re-checking the live response shape.
					if (!item.TryGetProperty("item", out trackElement) || trackElement.ValueKind == JsonValueKind.Null)
					{
						continue;
					}
					string trackName = "";
					JsonElement nameElement;
					if (trackElement.TryGetProperty("name", out nameElement))
					{
						trackName = nameElement.GetString();
					}
					if (string.IsNullOrEmpty(trackName))
					{
						continue;
					}
					string artistName = "";
					JsonElement artistsElement;
					if (trackElement.TryGetProperty("artists", out artistsElement) && artistsElement.GetArrayLength() > 0)
					{
						JsonElement firstArtist = artistsElement[0];
						JsonElement artistNameElement;
						if (firstArtist.TryGetProperty("name", out artistNameElement))
						{
							artistName = artistNameElement.GetString();
						}
					}
					PlaylistImportEntry entry = new PlaylistImportEntry();
					entry.Artist = artistName ?? "";
					entry.Title = trackName;
					tracks.Add(entry);
				}
				JsonElement nextEl;
				if (root.TryGetProperty("next", out nextEl) && nextEl.ValueKind != JsonValueKind.Null)
				{
					url = nextEl.GetString();
				}
				else
				{
					url = null;
				}
				doc.Dispose();
				Thread.Sleep(200);
			}
			return tracks;
		}

		private string SpotifyGet(string url)
		{
			if (!EnsureToken())
			{
				return null;
			}

			using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
			{
				request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", m_accessToken);

				using (HttpResponseMessage response = m_httpClient.Send(request))
				{
					if (!response.IsSuccessStatusCode)
					{
						string errorBody = ReadResponseBody(response);
						Log.Error(-1, "Spotify: GET failed " + response.StatusCode + " - " + url + " - " + errorBody);
						return null;
					}

					return ReadResponseBody(response);
				}
			}
		}

		private bool EnsureToken()
		{
			if (DateTime.UtcNow < m_tokenExpiry && !string.IsNullOrEmpty(m_accessToken))
			{
				return true;
			}

			if (string.IsNullOrEmpty(m_refreshToken))
			{
				return false;
			}

			string body = "grant_type=refresh_token"
				+ "&refresh_token=" + Uri.EscapeDataString(m_refreshToken);

			using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token"))
			{
				request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");

				string authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes(m_clientId + ":" + m_clientSecret));
				request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

				using (HttpResponseMessage response = m_httpClient.Send(request))
				{
					if (!response.IsSuccessStatusCode)
					{
						string errorBody = ReadResponseBody(response);
						Log.Error(-1, "Spotify: Token refresh failed - " + response.StatusCode + " - " + errorBody);
						return false;
					}

					string json = ReadResponseBody(response);
					JsonDocument doc = JsonDocument.Parse(json);
					JsonElement root = doc.RootElement;

					m_accessToken = root.GetProperty("access_token").GetString();
					int expiresIn = root.GetProperty("expires_in").GetInt32();
					m_tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

					// Spotify sometimes rotates the refresh token
					JsonElement newRefreshToken;
					if (root.TryGetProperty("refresh_token", out newRefreshToken))
					{
						m_refreshToken = newRefreshToken.GetString();
					}

					SaveCredentials();
					doc.Dispose();
					return true;
				}
			}
		}
		public static string GetCredentialBasePath()
		{
			string credentialRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse/Spotify");
			if (!Directory.Exists(credentialRoot))
			{
				Directory.CreateDirectory(credentialRoot);
			}
			return credentialRoot;

		}
		private void SaveCredentials()
		{
			JsonDocument doc = JsonDocument.Parse("{}");
			using (FileStream stream = File.Create(m_credentialPath))
			{
				Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
				writer.WriteStartObject();
				writer.WriteString("refresh_token", m_refreshToken);
				writer.WriteEndObject();
				writer.Flush();
			}
			doc.Dispose();
		}

		private void LoadCredentials()
		{
			if (!File.Exists(m_credentialPath))
			{
				return;
			}

			try
			{
				string json = File.ReadAllText(m_credentialPath);
				JsonDocument doc = JsonDocument.Parse(json);
				JsonElement root = doc.RootElement;

				JsonElement refreshElement;
				if (root.TryGetProperty("refresh_token", out refreshElement))
				{
					m_refreshToken = refreshElement.GetString();
				}

				doc.Dispose();
				Log.Info(-1, "Spotify: Loaded saved credentials");
			}
			catch (Exception ex)
			{
				Log.Error(-1, "Spotify: Failed to load credentials - " + ex.Message);
			}
		}

		private string ReadResponseBody(HttpResponseMessage response)
		{
			Stream stream = response.Content.ReadAsStream();
			StreamReader reader = new StreamReader(stream);
			string body = reader.ReadToEnd();
			reader.Dispose();
			return body;
		}
	}

	public class SpotifyPlaylistEntry
	{
		public string Id;
		public string Name;
		public int TrackCount;
	}
}
