using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pulse.Data;
using Pulse.MusicLibrary;
using Pulse.Protocols;
using Pulse.Spotify;
using Pulse.Protocols.Subsonic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using Pulse.Protocols.LegacyPulse;
using Pulse.Protocols.PulseAPI;
using Pulse.Database;
using Pulse.Series;
using Pulse.Services;

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
		private Subsonic m_subsonic;
		private PulseEndpoints m_pulseEndpoints;
		private Pulse.Protocols.LegacyPulse.LegacyPulseAPI m_legacyPulse;
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

		public void Run(IPulseRouteHost webServer, PulseConfig config)
		{
			m_config = config;

			// PulseData is the shared domain layer; build it first so both the
			// MusicManager facade and the auth endpoints hit the same instance.
			// MusicManager.LoadDB still owns the sqlite path setup and the
			// data.Load() call, so newing PulseData here is safe -- its
			// constructor is DB-independent.
			m_pulseData = new PulseData();
			m_musicManager = new MusicManager(config, m_pulseData);
			s_musicManager = m_musicManager;
			m_musicManager.Run(config.MusicPath);

			m_analyticsDB = new AnalyticsDB(config);

			m_podcastManager = new PodcastManager(config);
			m_podcastManager.Run();

			m_audiobookManager = new AudiobookManager(config);
			m_audiobookManager.Run();

			m_pulseEndpoints = new PulseEndpoints(this, m_musicManager, m_analyticsDB, m_podcastManager, m_audiobookManager);
			m_legacyPulse = new global::Pulse.Protocols.LegacyPulse.LegacyPulseAPI(this, m_musicManager);
			m_subsonic = new Subsonic(m_legacyPulse);
			m_authEndpoints = new AuthEndpoints(m_pulseData);

			RegisterRoutes(webServer);
			SyncSpotify();

			IsRunning = true;
		}
	
		private void RegisterRoutes(IPulseRouteHost host)
		{
			m_pulseEndpoints.RegisterRoutes(host);
			m_authEndpoints.RegisterRoutes(host);

			host.RegisterResultRoute("rest/ping.view", m_subsonic.HandlePing);
			host.RegisterResultRoute("rest/ping", m_subsonic.HandlePing);
			host.RegisterResultRoute("rest/getLicense.view", m_subsonic.HandleGetLicense);
			host.RegisterResultRoute("rest/getLicense", m_subsonic.HandleGetLicense);
			host.RegisterResultRoute("rest/getUser.view", m_subsonic.HandleGetUser);
			host.RegisterResultRoute("rest/getUser", m_subsonic.HandleGetUser);
			host.RegisterResultRoute("rest/getAlbumList2.view", m_subsonic.HandleGetAlbumList2);
			host.RegisterResultRoute("rest/getAlbumList2", m_subsonic.HandleGetAlbumList2);
			host.RegisterResultRoute("rest/getGenres.view", m_subsonic.HandleGetGenres);
			host.RegisterResultRoute("rest/getGenres", m_subsonic.HandleGetGenres);
			host.RegisterResultRoute("rest/getSongsByGenre.view", m_subsonic.HandleGetSongsByGenre);
			host.RegisterResultRoute("rest/getSongsByGenre", m_subsonic.HandleGetSongsByGenre);
			host.RegisterResultRoute("rest/getRandomSongs.view", m_subsonic.HandleGetRandomSongs);
			host.RegisterResultRoute("rest/getRandomSongs", m_subsonic.HandleGetRandomSongs);
			host.RegisterResultRoute("rest/download.view", m_subsonic.HandleDownload);
			host.RegisterResultRoute("rest/download", m_subsonic.HandleDownload);
			host.RegisterResultRoute("rest/getNowPlaying.view", m_subsonic.HandleGetNowPlaying);
			host.RegisterResultRoute("rest/getNowPlaying", m_subsonic.HandleGetNowPlaying);
			host.RegisterResultRoute("rest/getAlbumInfo.view", m_subsonic.HandleGetAlbumInfo);
			host.RegisterResultRoute("rest/getAlbumInfo", m_subsonic.HandleGetAlbumInfo);
			host.RegisterResultRoute("rest/getAlbumInfo2.view", m_subsonic.HandleGetAlbumInfo);
			host.RegisterResultRoute("rest/getAlbumInfo2", m_subsonic.HandleGetAlbumInfo);
			host.RegisterResultRoute("rest/getSimilarSongs.view", m_subsonic.HandleGetSimilarSongs2);
			host.RegisterResultRoute("rest/getSimilarSongs", m_subsonic.HandleGetSimilarSongs2);
			host.RegisterResultRoute("rest/getSimilarSongs2.view", m_subsonic.HandleGetSimilarSongs2);
			host.RegisterResultRoute("rest/getSimilarSongs2", m_subsonic.HandleGetSimilarSongs2);
			host.RegisterResultRoute("rest/getLyrics.view", m_subsonic.HandleGetLyrics);
			host.RegisterResultRoute("rest/getLyrics", m_subsonic.HandleGetLyrics);
			host.RegisterResultRoute("rest/getLyricsBySongId.view", m_subsonic.HandleGetLyricsBySongId);
			host.RegisterResultRoute("rest/getLyricsBySongId", m_subsonic.HandleGetLyricsBySongId);
			host.RegisterResultRoute("rest/getPlayQueue.view", m_subsonic.HandleGetPlayQueue);
			host.RegisterResultRoute("rest/getPlayQueue", m_subsonic.HandleGetPlayQueue);
			host.RegisterResultRoute("rest/savePlayQueue.view", m_subsonic.HandleSavePlayQueue);
			host.RegisterResultRoute("rest/savePlayQueue", m_subsonic.HandleSavePlayQueue);
			host.RegisterResultRoute("rest/getBookmarks.view", m_subsonic.HandleGetBookmarks);
			host.RegisterResultRoute("rest/getBookmarks", m_subsonic.HandleGetBookmarks);
			host.RegisterResultRoute("rest/createBookmark.view", m_subsonic.HandleCreateBookmark);
			host.RegisterResultRoute("rest/createBookmark", m_subsonic.HandleCreateBookmark);
			host.RegisterResultRoute("rest/deleteBookmark.view", m_subsonic.HandleDeleteBookmark);
			host.RegisterResultRoute("rest/deleteBookmark", m_subsonic.HandleDeleteBookmark);
			host.RegisterResultRoute("rest/getOpenSubsonicExtensions.view", m_subsonic.HandleGetOpenSubsonicExtensions);
			host.RegisterResultRoute("rest/getOpenSubsonicExtensions", m_subsonic.HandleGetOpenSubsonicExtensions);
			host.RegisterResultRoute("rest/getPlaylists.view", m_subsonic.HandleGetPlaylists);
			host.RegisterResultRoute("rest/getPlaylists", m_subsonic.HandleGetPlaylists);
			host.RegisterResultRoute("rest/getPlaylist.view", m_subsonic.HandleGetPlaylist);
			host.RegisterResultRoute("rest/getPlaylist", m_subsonic.HandleGetPlaylist);
			host.RegisterResultRoute("rest/getMusicFolders.view", m_subsonic.HandleGetMusicFolders);
			host.RegisterResultRoute("rest/getMusicFolders", m_subsonic.HandleGetMusicFolders);
			host.RegisterResultRoute("rest/getArtists.view", m_subsonic.HandleGetArtists);
			host.RegisterResultRoute("rest/getArtists", m_subsonic.HandleGetArtists);
			host.RegisterResultRoute("rest/getArtist.view", m_subsonic.HandleGetArtist);
			host.RegisterResultRoute("rest/getArtist", m_subsonic.HandleGetArtist);
			host.RegisterResultRoute("rest/getAlbum.view", m_subsonic.HandleGetAlbum);
			host.RegisterResultRoute("rest/getAlbum", m_subsonic.HandleGetAlbum);
			host.RegisterResultRoute("rest/stream.view", m_subsonic.HandleStream);
			host.RegisterResultRoute("rest/stream", m_subsonic.HandleStream);
			host.RegisterResultRoute("rest/getSong.view", m_subsonic.HandleGetSong);
			host.RegisterResultRoute("rest/getSong", m_subsonic.HandleGetSong);
			host.RegisterResultRoute("rest/getCoverArt.view", m_subsonic.HandleGetCoverArt);
			host.RegisterResultRoute("rest/getCoverArt", m_subsonic.HandleGetCoverArt);
			host.RegisterResultRoute("rest/scrobble.view", m_subsonic.HandleScrobble);
			host.RegisterResultRoute("rest/scrobble", m_subsonic.HandleScrobble);
			host.RegisterResultRoute("rest/getStarred.view", m_subsonic.HandleGetStarred);
			host.RegisterResultRoute("rest/getStarred", m_subsonic.HandleGetStarred);
			host.RegisterResultRoute("rest/getStarred2", m_subsonic.HandleGetStarred2);
			host.RegisterResultRoute("rest/getStarred2.view", m_subsonic.HandleGetStarred2);
			host.RegisterResultRoute("rest/getTopSongs.view", m_subsonic.HandleGetTopSongs);
			host.RegisterResultRoute("rest/getTopSongs", m_subsonic.HandleGetTopSongs);
			host.RegisterResultRoute("rest/getArtistInfo.view", m_subsonic.HandleGetArtistInfo);
			host.RegisterResultRoute("rest/getArtistInfo", m_subsonic.HandleGetArtistInfo);
			host.RegisterResultRoute("rest/getArtistInfo2.view", m_subsonic.HandleGetArtistInfo);
			host.RegisterResultRoute("rest/getArtistInfo2", m_subsonic.HandleGetArtistInfo);
			host.RegisterResultRoute("rest/search3.view", m_subsonic.HandleSearch3);
			host.RegisterResultRoute("rest/search3", m_subsonic.HandleSearch3);
			host.RegisterResultRoute("rest/getIndexes", m_subsonic.HandleGetIndexes);
			host.RegisterResultRoute("rest/getIndexes.view", m_subsonic.HandleGetIndexes);
			host.RegisterResultRoute("rest/getInternetRadioStations", m_subsonic.HandleGetInternetRadioStations);
			host.RegisterResultRoute("rest/getInternetRadioStations.view", m_subsonic.HandleGetInternetRadioStations);
			host.RegisterResultRoute("rest/getMusicDirectory", m_subsonic.HandleGetMusicDirectory);
			host.RegisterResultRoute("rest/getMusicDirectory.view", m_subsonic.HandleGetMusicDirectory);
			host.RegisterResultRoute("rest/setRating", m_subsonic.HandleSetRating);
			host.RegisterResultRoute("rest/setRating.view", m_subsonic.HandleSetRating);
			host.RegisterResultRoute("rest/star", m_subsonic.HandleStar);
			host.RegisterResultRoute("rest/star.view", m_subsonic.HandleStar);
			host.RegisterResultRoute("rest/unstar", m_subsonic.HandleUnstar);
			host.RegisterResultRoute("rest/unstar.view", m_subsonic.HandleUnstar);
			host.RegisterResultRoute("play", HandlePlay);
			host.RegisterResultRoute("rest/createPlaylist.view", m_subsonic.HandleCreatePlaylist);
			host.RegisterResultRoute("rest/createPlaylist", m_subsonic.HandleCreatePlaylist);
			host.RegisterResultRoute("rest/updatePlaylist.view", m_subsonic.HandleUpdatePlaylist);
			host.RegisterResultRoute("rest/updatePlaylist", m_subsonic.HandleUpdatePlaylist);
			host.RegisterResultRoute("rest/deletePlaylist.view", m_subsonic.HandleDeletePlaylist);
			host.RegisterResultRoute("rest/deletePlaylist", m_subsonic.HandleDeletePlaylist);

			//Pulse API
			host.RegisterResultRoute("pulse/recentlyPlayed", m_legacyPulse.HandleRecentlyPlayed);
			host.RegisterResultRoute("pulse/popularArtists", m_legacyPulse.HandlePopularArtists);
			host.RegisterResultRoute("pulse/topPlaylists", m_legacyPulse.GetTopPlaylists);
			host.RegisterResultRoute("pulse/recentPlaylists", m_legacyPulse.GetRecentPlaylists);
			host.RegisterResultRoute("pulse/artistTracks", m_legacyPulse.HandleArtistTracks);
			host.RegisterResultRoute("pulse/markPlaylistPlayed", m_legacyPulse.HandleMarkPlaylistPlayed);

			host.RegisterResultRoute("pulse/ping", m_legacyPulse.Ping);
			host.RegisterResultRoute("pulse/me", m_legacyPulse.GetUser);
			host.RegisterResultRoute("pulse/stream", m_legacyPulse.GetStream);
			host.RegisterResultRoute("pulse/download", m_legacyPulse.GetDownload);
			host.RegisterResultRoute("pulse/coverArt", m_legacyPulse.GetCoverArt);
			host.RegisterResultRoute("pulse/search", m_legacyPulse.Search);
			host.RegisterResultRoute("pulse/track", m_legacyPulse.GetTrack);
			host.RegisterResultRoute("pulse/topTracks", m_legacyPulse.GetTopTracks);
			host.RegisterResultRoute("pulse/artists", m_legacyPulse.GetArtists);
			host.RegisterResultRoute("pulse/artist", m_legacyPulse.GetArtist);
			host.RegisterResultRoute("pulse/albums", m_legacyPulse.GetAlbums);
			host.RegisterResultRoute("pulse/album", m_legacyPulse.GetAlbum);
			host.RegisterResultRoute("pulse/genres", m_legacyPulse.GetGenres);
			host.RegisterResultRoute("pulse/genreTracks", m_legacyPulse.GetGenreTracks);
			host.RegisterResultRoute("pulse/favorites", m_legacyPulse.GetFavorites);
			host.RegisterResultRoute("pulse/favorite", m_legacyPulse.Favorite);
			host.RegisterResultRoute("pulse/unfavorite", m_legacyPulse.Unfavorite);
			host.RegisterResultRoute("pulse/reportTrackAnalytics", m_legacyPulse.ReportTrackAnalytics);
			host.RegisterResultRoute("pulse/playlists", m_legacyPulse.GetPlaylists);
			host.RegisterResultRoute("pulse/playlist", m_legacyPulse.GetPlaylist);
			host.RegisterResultRoute("pulse/createPlaylist", m_legacyPulse.CreatePlaylist);
			host.RegisterResultRoute("pulse/updatePlaylist", m_legacyPulse.UpdatePlaylist);
			host.RegisterResultRoute("pulse/deletePlaylist", m_legacyPulse.DeletePlaylist);
			
			host.RegisterResultRoute("pulse/stats", HandleStats);
			host.RegisterRoute("web/stats.html", HandleStatsPage);

			host.RegisterResultRoute("pulse/version", HandleVersion);

			host.RegisterResultRoute("pulse/listUsers", m_legacyPulse.HandleListUsers);
			host.RegisterResultRoute("pulse/createUser", m_legacyPulse.HandleCreateUser);
			host.RegisterResultRoute("pulse/updateUser", m_legacyPulse.HandleUpdateUser);
			host.RegisterResultRoute("pulse/deleteUser", m_legacyPulse.HandleDeleteUser);
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
			string code = context.Request.Query["code"].FirstOrDefault();
			string state = context.Request.Query["state"].FirstOrDefault();

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
			string userName = context.Request.Query["u"].FirstOrDefault();

			List<TrackInfo> allTracks = m_musicManager.GetAllTracks();
			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			List<PlaylistInfo> allPlaylists = m_musicManager.GetAllPlaylists(userName);

			PulseStatsResponse stats = PulseStatsBuilder.Build(allTracks, allAlbums, allArtists, allPlaylists, userName);
			string json = System.Text.Json.JsonSerializer.Serialize(stats);
			return Results.Content(json, "application/json");
		}

		private IResult HandleVersion(HttpContext context)
		{
			return Results.Json(new { version = GetServerVersion() });
		}

		private void HandleStatsPage(HttpContext context)
		{
			string embed = context.Request.Query["embed"].FirstOrDefault();
			if (embed != "1")
			{
				string user = context.Request.Query["u"].FirstOrDefault();
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
			string embed = context.Request.Query["embed"].FirstOrDefault();
			if (embed != "1")
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
						Log.Info(-1, "Pulse: Started Spotify sync for " + userName);
					}
					else
					{
						Log.Warning(-1, "Pulse: Failed Spotify sync for " + userName);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error(-1, "Pulse: Spotify startup failed - " + ex.Message + "\n" + ex.StackTrace);
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
