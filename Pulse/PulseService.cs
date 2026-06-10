using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pulse.Data;
using Pulse.MusicLibrary;
using Pulse.Protocols;
using Pulse.Spotify;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using Pulse.Database;
using Pulse.Series;
using PulseAPI.CSharp;
using Pulse.DataStorage;
using Pulse.Podcasts;

namespace Pulse
{
	public interface IPulseRouteHost
	{
		void RegisterRoute(string path, Action<HttpContext> handler);
		void RegisterPrefixRoute(string path, Action<HttpContext> handler);
		void RegisterResultRoute(string path, Func<HttpContext, IResult> handler);
	}

	public class PulseService
	{
		public MusicManager GetMusicManager()
		{
			return m_musicManager;
		}

		public static PulseConfig GetConfig()
		{
			return m_config;
		}

		// Returns false until the initial music library scan has finished.
		// HttpServer.HandleRequest uses this to short-circuit incoming requests
		// with a loading page while the database is still being populated --
		// otherwise concurrent reads against the in-flight scan race against
		// the dictionaries it's mutating.
		public static bool IsReady()
		{
			return s_musicManager != null && !s_musicManager.GetIsScanning();
		}

		// Server version as set by /p:Version at publish time (CI). Falls back
		// to whatever AssemblyInformationalVersion the local build produced
		// (csproj default = "0.0.0-local"). Strips any "+<sha>" suffix that
		// the .NET SDK appends so the web tag stays compact (Flatline #229).
		public static string GetServerVersion()
		{
			System.Reflection.AssemblyInformationalVersionAttribute attr =
				(System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
					typeof(PulseService).Assembly,
					typeof(System.Reflection.AssemblyInformationalVersionAttribute));
			if (attr == null) { return "dev"; }
			string version = attr.InformationalVersion;
			if (string.IsNullOrEmpty(version)) { return "dev"; }
			int plus = version.IndexOf('+');
			if (plus > 0)
			{
				version = version.Substring(0, plus);
			}
			return version;
		}

		static PulseConfig m_config;
		static MusicManager s_musicManager;

		private PulseEndpoints m_pulseEndpoints;

		private PulseData m_pulseData;
		private MusicManager m_musicManager;
		private AnalyticsDB m_analyticsDB;
		private PodcastManager m_podcastManager;
		private AudiobookManager m_audiobookManager;
		private AuthEndpoints m_authEndpoints;
		private Dictionary<string, SpotifySync> m_spotifySyncs = new Dictionary<string, SpotifySync>();
		private object m_spotifySyncsLock = new object();
		// Pending Spotify OAuth attempts, keyed by a server-issued random state
		// nonce -> the user the flow was started for, with an expiry. The callback
		// validates the returned state against this instead of trusting it as the
		// username (FL#310).
		private Dictionary<string, PendingSpotifyAuth> m_spotifyAuthStates = new Dictionary<string, PendingSpotifyAuth>();
		private object m_spotifyAuthStatesLock = new object();


		public bool IsRunning { get; private set; }

		/// <summary>
		/// Stop background services and flush dirty data. Call once on process exit.
		/// </summary>
		public void Shutdown()
		{
			foreach (SpotifySync sync in m_spotifySyncs.Values)
			{
				sync.Stop();
			}
			m_pulseData.Shutdown();
			m_podcastManager.Shutdown();
			m_audiobookManager.Shutdown();
		}

		public void Run(IPulseRouteHost webServer, PulseConfig config)
		{
			m_config = config;


			m_pulseData = new PulseData(m_config);
			m_pulseData.Load();

			m_musicManager = new MusicManager(config, m_pulseData);
			s_musicManager = m_musicManager;
			m_musicManager.Run(config.MusicPath);

			m_analyticsDB = new AnalyticsDB(config);

			m_podcastManager = new PodcastManager(config);
			m_podcastManager.Run();

			m_audiobookManager = new AudiobookManager(config);
			m_audiobookManager.Run();

			m_pulseEndpoints = new PulseEndpoints(this, m_musicManager, m_analyticsDB, m_podcastManager, m_audiobookManager);
			
			m_authEndpoints = new AuthEndpoints(m_pulseData);

			RegisterRoutes(webServer);
			SyncSpotify();

			IsRunning = true;
		}
	
		private void RegisterRoutes(IPulseRouteHost host)
		{
			m_pulseEndpoints.RegisterRoutes(host);
			m_authEndpoints.RegisterRoutes(host);

			host.RegisterResultRoute("pulse/stats", HandleStats);

			host.RegisterResultRoute("pulse/version", HandleVersion);

			//web pages
			host.RegisterRoute("web/stats.html", HandleStatsPage);
			host.RegisterRoute("web/settings.html", HandleSettingsPage);


			host.RegisterRoute("spotify/callback", HandleSpotifyCallback);
			host.RegisterPrefixRoute("spotify/authorize", HandleSpotifyAuthorize);
		}

		private class PendingSpotifyAuth
		{
			public string UserName;
			public DateTime ExpiresUtc;
		}

		private void HandleSpotifyCallback(HttpContext context)
		{
			string code = QueryParameters.GetString(context, "code");
			string state = QueryParameters.GetString(context, "state");

			if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
			{
				context.Response.StatusCode = 400;
				byte[] errorBytes = System.Text.Encoding.UTF8.GetBytes("Missing code or state parameter");
				context.Response.Body.Write(errorBytes, 0, errorBytes.Length);
				return;
			}

			// Validate the state against a server-issued nonce instead of trusting
			// it as the username. Single-use: consume it on lookup (FL#310).
			string userName = null;
			lock (m_spotifyAuthStatesLock)
			{
				PruneExpiredAuthStates();
				PendingSpotifyAuth pending;
				if (m_spotifyAuthStates.TryGetValue(state, out pending))
				{
					m_spotifyAuthStates.Remove(state);
					userName = pending.UserName;
				}
			}

			if (string.IsNullOrEmpty(userName))
			{
				context.Response.StatusCode = 400;
				byte[] badState = System.Text.Encoding.UTF8.GetBytes("Invalid or expired authorization state. Start the Spotify connection again.");
				context.Response.Body.Write(badState, 0, badState.Length);
				return;
			}

			SpotifySync sync = GetOrCreateSpotifySync(userName);
			bool success = sync.HandleCallback(code);
			if (success)
			{
				sync.Start();
				byte[] successBytes = System.Text.Encoding.UTF8.GetBytes("Spotify authorized for " + userName + "! You can close this window.");
				context.Response.Body.Write(successBytes, 0, successBytes.Length);
			}
			else
			{
				context.Response.StatusCode = 500;
				byte[] failBytes = System.Text.Encoding.UTF8.GetBytes("Authorization failed. Check server logs.");
				context.Response.Body.Write(failBytes, 0, failBytes.Length);
			}
		}

		private void HandleSpotifyAuthorize(HttpContext context)
		{
			string userName = context.Request.Path.Value.Split('/').Last();
			if (string.IsNullOrEmpty(userName))
			{
				context.Response.StatusCode = 400;
				return;
			}

			// Issue a random state nonce and remember which user it belongs to, so
			// the callback can't be tricked into binding tokens to another account
			// by supplying an arbitrary state (FL#310).
			string nonce = GenerateStateNonce();
			PendingSpotifyAuth pending = new PendingSpotifyAuth();
			pending.UserName = userName;
			pending.ExpiresUtc = DateTime.UtcNow.AddMinutes(10);
			lock (m_spotifyAuthStatesLock)
			{
				PruneExpiredAuthStates();
				m_spotifyAuthStates[nonce] = pending;
			}

			SpotifySync sync = GetOrCreateSpotifySync(userName);
			string url = sync.GetAuthorizationUrl(nonce);
			context.Response.Redirect(url);
		}

		private static string GenerateStateNonce()
		{
			byte[] bytes = new byte[32];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(bytes);
			}
			return Convert.ToHexString(bytes).ToLowerInvariant();
		}

		// Caller must hold m_spotifyAuthStatesLock.
		private void PruneExpiredAuthStates()
		{
			DateTime now = DateTime.UtcNow;
			List<string> expired = new List<string>();
			foreach (KeyValuePair<string, PendingSpotifyAuth> entry in m_spotifyAuthStates)
			{
				if (entry.Value.ExpiresUtc <= now)
				{
					expired.Add(entry.Key);
				}
			}
			for (int idx = 0; idx < expired.Count; idx++)
			{
				m_spotifyAuthStates.Remove(expired[idx]);
			}
		}

		private IResult HandleStats(HttpContext context)
		{
			string userName = QueryParameters.GetString(context, "u");

			List<TrackData> allTracks = m_musicManager.GetAllTracks();
			List<AlbumData> allAlbums = m_musicManager.GetAllAlbums();
			List<ArtistData> allArtists = m_musicManager.GetAllArtists();
			List<PlaylistData> allPlaylists = m_musicManager.GetAllPlaylists(userName);

			PulseStats stats = PulseStatsBuilder.Build(allTracks, allAlbums, allArtists, allPlaylists, userName);
			string json = System.Text.Json.JsonSerializer.Serialize(stats);
			return Results.Content(json, "application/json");
		}

		private IResult HandleVersion(HttpContext context)
		{
			return Results.Json(new { version = GetServerVersion() });
		}

		private void HandleStatsPage(HttpContext context)
		{
			if (!QueryParameters.GetBool(context, "embed"))
			{
				string user = QueryParameters.GetString(context, "u");
				string redirect = "/web/pulse.html?view=stats";
				if (!string.IsNullOrEmpty(user))
				{
					redirect = redirect + "&u=" + System.Uri.EscapeDataString(user);
				}
				context.Response.Redirect(redirect);
				return;
			}
			string htmlPath = Path.Combine(AppContext.BaseDirectory, "Content", "Web", "stats.html");
			if (!File.Exists(htmlPath))
			{
				context.Response.StatusCode = 404;
				byte[] notFound = System.Text.Encoding.UTF8.GetBytes("Stats page not found");
				context.Response.Body.Write(notFound, 0, notFound.Length);
				return;
			}
			byte[] htmlBytes = File.ReadAllBytes(htmlPath);
			context.Response.ContentType = "text/html";
			context.Response.Body.Write(htmlBytes, 0, htmlBytes.Length);
		}

		private void HandleSettingsPage(HttpContext context)
		{
			if (!QueryParameters.GetBool(context, "embed"))
			{
				context.Response.Redirect("/web/pulse.html?view=settings");
				return;
			}
			string htmlPath = Path.Combine(AppContext.BaseDirectory, "Content", "Web", "settings.html");
			if (!File.Exists(htmlPath))
			{
				context.Response.StatusCode = 404;
				byte[] notFound = System.Text.Encoding.UTF8.GetBytes("Settings page not found");
				context.Response.Body.Write(notFound, 0, notFound.Length);
				return;
			}
			byte[] htmlBytes = File.ReadAllBytes(htmlPath);
			context.Response.ContentType = "text/html";
			context.Response.Body.Write(htmlBytes, 0, htmlBytes.Length);
		}

		private void SyncSpotify()
		{
			try
			{
				string spotifyCredentialBase = SpotifySync.GetCredentialBasePath();

				string[] credFiles = Directory.GetFiles(spotifyCredentialBase, "spotify_*.json");
				for (int index = 0; index < credFiles.Length; index++)
				{
					
					string fileName = Path.GetFileNameWithoutExtension(credFiles[index]);
					string userName = fileName.Substring("spotify_".Length);
					SpotifySync sync = GetOrCreateSpotifySync(userName);
					if (sync.IsAuthorized())
					{
						sync.Start();
						Log.Info("Pulse: Started Spotify sync for " + userName);
					}
					else
					{
						Log.Warning("Pulse: Failed Spotify sync for " + userName);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("Pulse: Spotify startup failed - " + ex.Message + "\n" + ex.StackTrace);
			}
		}
		private SpotifySync GetOrCreateSpotifySync(string userName)
		{
			// Invoked from concurrent request threads (spotify/callback,
			// spotify/authorize) as well as startup. Guard the get-or-add so two
			// concurrent first-time requests for the same user can't both miss the
			// lookup and then race on Dictionary.Add (throws / corrupts state) (#312).
			lock (m_spotifySyncsLock)
			{
				SpotifySync sync;
				if (m_spotifySyncs.TryGetValue(userName, out sync))
				{
					return sync;
				}

				string credentialPath = Path.Combine(SpotifySync.GetCredentialBasePath(), "spotify_" + userName + ".json");

				sync = new SpotifySync(userName, m_musicManager, m_config.SpotifyClient, m_config.SpotifySecret, m_config.SpotifyRedirectURI, credentialPath);
				m_spotifySyncs.Add(userName, sync);
				return sync;
			}
		}

		private IResult HandlePlay(HttpContext context)
		{
			try
			{
				string file = null;
				string[] dirs = Directory.GetDirectories(m_config.MusicPath);
				foreach (string dir in dirs)
				{
					string[] files = Directory.GetFiles(dir, "*.mp3", SearchOption.TopDirectoryOnly);
					if (files.Length > 0)
					{
						file = files[0];
						break;
						
					}
				}
				if (file == null)
				{
					return Results.NotFound("No mp3 files found");
				}
				FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
				return Results.File(fileStream, "audio/mpeg", enableRangeProcessing: true);
			}
			catch (Exception ex)
			{
				return Results.NotFound(ex.Message);
			}
		}

		public static IPAddress GetLanIPAddress()
		{
			NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

			for (int i = 0; i < interfaces.Length; i++)
			{
				NetworkInterface networkInterface = interfaces[i];

				if (networkInterface.OperationalStatus != OperationalStatus.Up)
				{
					continue;
				}

				if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
				{
					continue;
				}

				// Skip vEthernet, Docker, WSL, etc.
				string networkInterfaceName = networkInterface.Name.ToLowerInvariant();
				if (networkInterfaceName.Contains("virtual") || networkInterfaceName.Contains("vethernet") || networkInterfaceName.Contains("docker"))
				{
					continue;
				}

				IPInterfaceProperties properties = networkInterface.GetIPProperties();
				UnicastIPAddressInformationCollection unicast = properties.UnicastAddresses;

				for (int j = 0; j < unicast.Count; j++)
				{
					UnicastIPAddressInformation address = unicast[j];

					if (address.Address.AddressFamily == AddressFamily.InterNetwork)
					{
						return address.Address;
					}
				}
			}

			return IPAddress.Loopback; // fallback
		}
	}
}
