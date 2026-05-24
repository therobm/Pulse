using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pulse.Data;
using Pulse.MusicLibrary;
using Pulse.Protocols;
using Pulse.Spotify;
using Pulse.SubsonicService;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;


namespace Pulse
{
	public interface IPulseRouteHost
	{
		void RegisterRoute(string path, Action<HttpContext> handler);
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

		static PulseConfig m_config;
		static MusicManager s_musicManager;
		private Subsonic m_subsonic;
		private PulseAPI m_pulseAPI;
		private MusicManager m_musicManager;
		private Dictionary<string, SpotifySync> m_spotifySyncs = new Dictionary<string, SpotifySync>();


		public bool IsRunning { get; private set; }

		public void Run(IPulseRouteHost webServer, PulseConfig config)
		{
			m_config = config;

			m_musicManager = new MusicManager(config);
			s_musicManager = m_musicManager;
			m_musicManager.Run(config.MusicPath);

			m_subsonic = new Subsonic(this, m_musicManager);
			m_pulseAPI = new PulseAPI(this, m_musicManager);

			RegisterRoutes(webServer);
			SyncSpotify();

			IsRunning = true;
		}
	
		private void RegisterRoutes(IPulseRouteHost host)
		{
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
			host.RegisterResultRoute("pulse/recentlyPlayed", m_pulseAPI.HandleRecentlyPlayed);
			host.RegisterResultRoute("pulse/popularArtists", m_pulseAPI.HandlePopularArtists);
			host.RegisterResultRoute("pulse/topPlaylists", m_pulseAPI.HandleTopPlaylists);
			host.RegisterResultRoute("rest/playRandom", m_subsonic.HandlePlayRandom);

			host.RegisterResultRoute("pulse/stats", HandleStats);
			host.RegisterRoute("pulse/stats.html", HandleStatsPage);


			host.RegisterRoute("spotify/callback", HandleSpotifyCallback);
			host.RegisterRoute("spotify/authorize", HandleSpotifyAuthorize);
		}

		private void HandleSpotifyCallback(HttpContext context)
		{
			string code = context.Request.Query["code"].FirstOrDefault();
			string userName = context.Request.Query["state"].FirstOrDefault();

			if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(userName))
			{
				context.Response.StatusCode = 400;
				byte[] errorBytes = System.Text.Encoding.UTF8.GetBytes("Missing code or state parameter");
				context.Response.Body.Write(errorBytes, 0, errorBytes.Length);
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
			SpotifySync sync = GetOrCreateSpotifySync(userName);
			string url = sync.GetAuthorizationUrl(userName);
			context.Response.Redirect(url);
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

		private void HandleStatsPage(HttpContext context)
		{
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
						Console.WriteLine("Pulse: Started Spotify sync for " + userName);
					}
					else
					{
						Console.WriteLine("Pulse: Failed Spotify sync for " + userName);
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Pulse: Spotify startup failed - " + ex.Message + "\n" + ex.StackTrace);
			}
		}
		private SpotifySync GetOrCreateSpotifySync(string userName)
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
