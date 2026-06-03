using Pulse;
using Pulse.Data;
using Pulse.Lidarr;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Pulse.MusicLibrary
{
	public class PlaylistImportEntry
	{
		public string Artist { get; set; }
		public string Title { get; set; }
	}

	public class MusicManager
	{
		public Dictionary<string, ArtistInfo> m_scanningArtistCache = new Dictionary<string, ArtistInfo>();
		public Dictionary<string, AlbumInfo> m_scanningAlbumCache = new Dictionary<string, AlbumInfo>();

		public int GetTrackCount()
		{
			return m_database.GetTrackCount();
		}
		public int GetAlbumCount()
		{
			return m_database.GetAlbumCount();
		}
		public int GetArtistCount()
		{
			return m_database.GetArtistCount();
		}

		public bool GetIsScanning()
		{
			return m_scanning;
		}

		public List<ArtistInfo> GetAllArtists()
		{
			return m_database.GetAllArtists();
		}

		public List<TrackInfo> GetAllTracks()
		{
			return m_database.GetAllTracks();
		}
		public List<AlbumInfo> GetAllAlbums()
		{
			return m_database.GetAllAlbums();
		}

	
		public PlaylistInfo GetPlaylist(string id)
		{
			return m_database.GetPlaylist(id);
		}
		public PlaylistAndTracks GetPlaylistAndTracks(string id)
		{
			PlaylistInfo playlist = GetPlaylist(id);
			List<TrackInfo> tracks = GetPlaylistTracks(id);

			PlaylistAndTracks fullPlaylist = new PlaylistAndTracks(playlist, tracks);
			return fullPlaylist;
		}
		public List<PlaylistInfo> GetAllPlaylists(string userName)
		{
			return m_database.GetAllPlaylists(userName);
		}

		public List<TrackInfo> GetPlaylistTracks(string playlistId)
		{
			return m_database.GetPlaylistTracks(playlistId);
		}

		public void SetRating(string trackId, int rating)
		{
			m_database.SetRating(trackId, rating);
		}

		public void UpdateStar(string userName, string trackId, string albumId, string artistId, bool isStarred)
		{
			m_database.UpdateStar(userName, trackId, albumId, artistId, isStarred);
		}

		public PulseAnalyticsInfo GetAnalytics()
		{
			return m_database.GetAnalytics();
		}

		/// <summary>
		/// Pass-through to the analytics-event rollup used by the topItems /
		/// recentlyPlayed routes to rank one media type by play count or recency.
		/// </summary>
		public Dictionary<string, AnalyticsAggregate> GetStartedAggregates(string userName, eDataType mediaType)
		{
			return m_database.GetStartedAggregates(userName, mediaType);
		}

		public TrackInfo GetTrack(string id)
		{
			return m_database.GetTrack(id);
		}

		public AlbumInfo GetAlbum(string id)
		{
			return m_database.GetAlbum(id);
		}

		public ArtistInfo GetArtist(string id)
		{
			return m_database.GetArtist(id);
		}


		public List<BookmarkInfo> GetBookmarks(string userName)
		{
			return m_database.GetBookmarks(userName);
		}

		public void SaveBookmark(string userName, string trackId, long positionMs, string comment)
		{
			m_database.SaveBookmark(userName, trackId, positionMs, comment);
		}

		public void DeleteBookmark(string userName, string trackId)
		{
			m_database.DeleteBookmark(userName, trackId);
		}

		public List<UserRecord> GetAllUsers()
		{
			return m_database.GetAllUsers();
		}

		public UserRecord GetUser(string name)
		{
			return m_database.GetUser(name);
		}

		public string CreateUser(string name, string displayName, bool isAdmin)
		{
			return m_database.CreateUser(name, displayName, isAdmin);
		}

		public string UpdateUser(string oldName, string newName, string displayName, bool isAdmin)
		{
			return m_database.UpdateUser(oldName, newName, displayName, isAdmin);
		}

		public void DeleteUser(string userName)
		{
			m_database.DeleteUser(userName);
		}

		public bool GetAlbumCover(AlbumInfo album, out byte[] bytes, out string contentType)
		{
			bytes = null;
			contentType = "image/jpeg";
			if (album == null || album.Tracks.Count == 0)
			{
				return false;
			}

			for (int index = 0; index < album.Tracks.Count; index++)
			{
				try
				{
					TagLib.File tagFile = TagLib.File.Create(album.Tracks[index].FilePath);
					if (tagFile.Tag.Pictures.Length > 0)
					{
						bytes = tagFile.Tag.Pictures[0].Data.Data;
						tagFile.Dispose();
						return true;
					}
					tagFile.Dispose();
				}
				catch (Exception ex)
				{
					Log.Error(-1, "TryGetAlbumCoverBytes: failed to read embedded art - " + ex.Message);
				}
			}

			string albumDir = Path.GetDirectoryName(album.Tracks[0].FilePath);
			string[] artFileNames = new string[] { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png", "album.jpg", "album.png" };
			for (int artIndex = 0; artIndex < artFileNames.Length; artIndex++)
			{
				string artPath = Path.Combine(albumDir, artFileNames[artIndex]);
				if (File.Exists(artPath))
				{
					bytes = File.ReadAllBytes(artPath);
					if (artPath.EndsWith(".png"))
					{
						contentType = "image/png";
					}
					return true;
				}
			}

			return false;
		}

		public bool GetArtistImage(ArtistInfo artist, out byte[] bytes, out string contentType)
		{
			bytes = null;
			contentType = "image/jpeg";
			if (artist == null)
				return false;

			// Score each album by its total play count across tracks and pick the
			// busiest. Ties go to the order the artist has albums in. New albums
			// (zero plays) still get a chance through the fallback loop below.
			AlbumInfo bestAlbum = null;
			int bestPlays = -1;
			for (int index = 0; index < artist.Albums.Count; index++)
			{
				AlbumInfo album = artist.Albums[index];
				int plays = 0;
				for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
				{
					plays = plays + album.Tracks[trackIndex].Score.PlayCount;
				}
				if (plays > bestPlays)
				{
					bestPlays = plays;
					bestAlbum = album;
				}
			}

			if (bestAlbum != null)
			{
				return GetAlbumCover(bestAlbum, out bytes, out contentType);
			}

			// Fallback: walk every album until we find one with art.
			for (int index = 0; index < artist.Albums.Count; index++)
			{
				if (artist.Albums[index] == bestAlbum)
				{
					continue;
				}

				if (GetAlbumCover(artist.Albums[index], out bytes, out contentType))
				{
					return true;
				}
			}
			return false;
		}



		private IPulseDatabase m_database;
		private object m_missingLock = new object();
		private LidarrSync m_lidarrSync;
		private Thread m_scanThread;
		private bool m_scanning;
		private object m_scanLock = new object();
		private int m_processedSinceLastSave = 0;
		private string m_nowPlayingTrackId;
		private DateTime m_nowPlayingStartTime = DateTime.MinValue;
		private HashSet<string> m_missingSongs = new HashSet<string>();
		private PulseConfig m_config;
		public MusicManager(PulseConfig config)
		{
			m_config = config;
			m_lidarrSync = new LidarrSync(config.LidarrURL, config.LidarrApiKey);
		}

		public static string GenerateID(string input)
		{
			using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
			{
				byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
				byte[] hashBytes = md5.ComputeHash(inputBytes);
				return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
			}
		}

		public void Run(string musicPath)
		{
			if (m_scanning)
			{
				return;
			}

			LoadDB();
			DebugDuplicates();
			m_pendingScanPath = musicPath;
			m_scanThread = new Thread(RunScanThread);
			m_scanThread.IsBackground = true;
			m_scanThread.Name = "Pulse.MusicScan";
			m_scanning = true;
			m_scanThread.Start();
		}

		private string m_pendingScanPath;

		private void RunScanThread()
		{
			RunScan(m_pendingScanPath);
		}

		public void OnPlaylistSyncComplete()
		{
			HashSet<string> missingSongsSnapshot;
			lock (m_missingLock)
			{
				missingSongsSnapshot = new HashSet<string>(m_missingSongs);
			}
			m_lidarrSync.RequestArtists(missingSongsSnapshot);
		}

		public void OnTrackStreamed(string userName, string trackId)
		{
			if (m_nowPlayingTrackId == trackId)
			{
				return;
			}

			if (m_nowPlayingTrackId != null && m_nowPlayingTrackId != trackId)
			{
				TrackInfo previousTrack = m_database.GetTrack(m_nowPlayingTrackId);
				if (previousTrack != null)
				{
					double elapsedSeconds = (DateTime.UtcNow - m_nowPlayingStartTime).TotalSeconds;
					double listenSeconds = Math.Min(elapsedSeconds, previousTrack.DurationSeconds);
					previousTrack.Score.TotalListenSeconds += listenSeconds;
					if (userName != null)
					{
						if (!previousTrack.UserScore.ContainsKey(userName))
						{
							previousTrack.UserScore.Add(userName, new ScoreData());
						}
						previousTrack.UserScore[userName].TotalListenSeconds += listenSeconds;
					}

					float threshold = previousTrack.DurationSeconds * 0.5f;

					previousTrack.Score.PlayCount = previousTrack.Score.PlayCount + 1;
					previousTrack.LastPlayed = DateTime.UtcNow;
					if (previousTrack.ParentArtist != null)
					{
						previousTrack.ParentArtist.LastPlayed = DateTime.UtcNow;
						previousTrack.ParentArtist.m_bIsDirty = true;
					}

					if (userName != null)
					{
						previousTrack.UserScore[userName].PlayCount = previousTrack.UserScore[userName].PlayCount + 1;
					}
					if (elapsedSeconds < threshold)
					{
						previousTrack.Score.SkipCount = previousTrack.Score.SkipCount + 1;
						if (userName != null)
						{
							previousTrack.UserScore[userName].SkipCount = previousTrack.UserScore[userName].SkipCount + 1;
						}
					}

					PulseAnalyticsInfo analytics = m_database.GetAnalytics();
					analytics.RecentlyPlayed.Remove(m_nowPlayingTrackId);
					analytics.RecentlyPlayed.Insert(0, m_nowPlayingTrackId);
					if (analytics.RecentlyPlayed.Count > 50)
					{
						analytics.RecentlyPlayed.RemoveAt(analytics.RecentlyPlayed.Count - 1);
					}
					analytics.m_bIsDirty = true;

					RecalculateScore(previousTrack);

					bool skipped = elapsedSeconds < threshold;
					string skipSuffix = "";
					if (skipped)
					{
						skipSuffix = " SKIPPED";
					}
					Log.Info(-1, "Finalized: " + previousTrack.Artist + ":" + previousTrack.Title
						+ " elapsed=" + elapsedSeconds.ToString("F1") + "s"
						+ " duration=" + previousTrack.DurationSeconds + "s"
						+ " listen=" + listenSeconds.ToString("F1") + "s"
						+ " plays=" + previousTrack.Score.PlayCount
						+ " skips=" + previousTrack.Score.SkipCount
						+ " score=" + previousTrack.Score.WeightedScore.ToString("F3")
						+ skipSuffix);

					SaveDB("track-streamed");
				}
			}

			m_nowPlayingTrackId = trackId;
			m_nowPlayingStartTime = DateTime.UtcNow;

			TrackInfo newTrack = m_database.GetTrack(m_nowPlayingTrackId);
			if (newTrack != null)
			{
				string user = "na";
				if (!string.IsNullOrEmpty(userName))
				{
					user = userName;
				}
				Log.Info(-1, "[" + user + "] Streaming: " + newTrack.Artist + ":" + newTrack.Title);
			}
		}

		/// <summary>
		/// Entry point for the client analytics feed (pulse_v1/reportAnalytics).
		/// Every observed playback state change is appended to the immutable event
		/// log -- the substrate the topItems / recentlyPlayed routes aggregate
		/// over for play-count and recency ranking. A 'Started' event also bumps
		/// the in-memory last-played for its subject so the non-aggregating
		/// consumers (BuildPlaylist's LastPlayed field, the legacy recents routes,
		/// track scoring) stay current: a Track Started drives the now-playing
		/// state machine, while Album/Artist/Playlist Starts update last-played
		/// directly (this replaces the retired markPlaylistPlayed call). Albums
		/// carry no in-memory last-played field, so an album's recency is read
		/// back from the event log by the routes that need it.
		/// </summary>
		public void OnAnalyticsEvent(string userName, PulseAnalytics analytics)
		{
			if (analytics == null || string.IsNullOrEmpty(analytics.MediaId))
			{
				return;
			}

			m_database.RecordAnalyticsEvent(userName, analytics, DateTime.UtcNow);

			if (analytics.Action != PulseAnalytics.eAction.Started)
			{
				return;
			}

			switch (analytics.MediaType)
			{
				case eDataType.Track:
					OnTrackStreamed(userName, analytics.MediaId);
					break;
				case eDataType.Artist:
					OnArtistStarted(userName, analytics.MediaId);
					break;
				case eDataType.Playlist:
					OnPlaylistStarted(userName, analytics.MediaId);
					break;
			}
		}

		/// <summary>
		/// Bumps an artist's aggregate last-played when a collection-level Artist
		/// Started arrives (e.g. "play this artist" from a client). Track Starts
		/// already bump the parent artist via OnTrackStreamed; this covers the
		/// case where the artist itself is the played subject.
		/// </summary>
		private void OnArtistStarted(string userName, string artistId)
		{
			ArtistInfo artist = m_database.GetArtist(artistId);
			if (artist == null)
			{
				return;
			}
			artist.LastPlayed = DateTime.UtcNow;
			artist.m_bIsDirty = true;
			SaveDB("artist-started");
		}

		/// <summary>
		/// Advances a playlist's aggregate and per-user last-played when a
		/// Playlist Started arrives. This is the pulse_v1 replacement for the
		/// retired markPlaylistPlayed route -- without it, played playlists stop
		/// advancing and silently drop out of the Recently Played feed.
		/// </summary>
		private void OnPlaylistStarted(string userName, string playlistId)
		{
			PlaylistInfo playlist = m_database.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return;
			}
			DateTime now = DateTime.UtcNow;
			playlist.LastPlayed = now;
			if (!string.IsNullOrEmpty(userName))
			{
				playlist.UserLastPlayed[userName] = now;
			}
			playlist.m_bIsDirty = true;
			SaveDB("playlist-started");
		}

		public PlaylistInfo ImportPlaylist(string name, List<PlaylistImportEntry> entries)
		{
			//Console.WriteLine("Importing playlist: " + name);
			string playlistId = MusicManager.GenerateID("playlist/" + name);
			PlaylistInfo existing = m_database.GetPlaylist(playlistId);

			PlaylistInfo playlist = new PlaylistInfo();
			playlist.Id = playlistId;
			playlist.Name = name;

			int matched = 0;
			int missed = 0;
			long totalDuration = 0;

			List<TrackInfo> tracks = m_database.GetAllTracks();
			string[] normalizedTrackArtists = new string[tracks.Count];
			string[] normalizedTrackTitles = new string[tracks.Count];
			List<string>[] splitTrackArtists = new List<string>[tracks.Count];
			for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
			{
				normalizedTrackArtists[trackIndex] = NormalizeForMatch(tracks[trackIndex].Artist);
				normalizedTrackTitles[trackIndex] = NormalizeTitle(tracks[trackIndex].Title, normalizedTrackArtists[trackIndex]);
				splitTrackArtists[trackIndex] = SplitArtists(normalizedTrackArtists[trackIndex]);
			}

			// Collect the source-order matched ids and durations, then merge with
			// any prior manual order so that re-syncs don't clobber the order the
			// user has set locally via drag-reorder. New tracks appear at the end;
			// removed tracks drop out.
			List<string> sourceOrderIds = new List<string>();
			Dictionary<string, long> durationsById = new Dictionary<string, long>();

			for (int index = 0; index < entries.Count; index++)
			{
				string entryArtist = NormalizeForMatch(entries[index].Artist);
				string entryTitle = NormalizeTitle(entries[index].Title, entryArtist);
				List<string> entryArtistParts = SplitArtists(entryArtist);
				TrackInfo bestMatch = null;
				int bestScore = int.MaxValue;
				for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
				{
					if (!ArtistsOverlap(entryArtistParts, splitTrackArtists[trackIndex]))
					{
						continue;
					}
					int distance = LevenshteinDistance(entryTitle, normalizedTrackTitles[trackIndex]);
					if (distance < bestScore)
					{
						bestScore = distance;
						bestMatch = tracks[trackIndex];
					}
				}
				int threshold = Math.Max(3, entryTitle.Length / 4);
				if (bestMatch != null && bestScore <= threshold)
				{
					if (!durationsById.ContainsKey(bestMatch.Id))
					{
						sourceOrderIds.Add(bestMatch.Id);
						durationsById[bestMatch.Id] = bestMatch.DurationSeconds;
					}
					matched++;
					if (bestMatch.Score.PlayCount == 0)
					{
						float playTime = 0.8f;
						bestMatch.Score.PlayCount++;
						bestMatch.Score.TotalListenSeconds += bestMatch.DurationSeconds * playTime;
						RecalculateScore(bestMatch);
					}
				}
				else
				{
					lock (m_missingLock)
					{
						m_missingSongs.Add(entries[index].Artist + " - " + entries[index].Title);
					}
					missed++;
				}
			}

			HashSet<string> placed = new HashSet<string>();
			if (existing != null && existing.TrackIds != null)
			{
				HashSet<string> matchedSet = new HashSet<string>(sourceOrderIds);
				for (int index = 0; index < existing.TrackIds.Count; index++)
				{
					string id = existing.TrackIds[index];
					if (matchedSet.Contains(id) && !placed.Contains(id))
					{
						playlist.TrackIds.Add(id);
						placed.Add(id);
					}
				}
			}
			for (int index = 0; index < sourceOrderIds.Count; index++)
			{
				string id = sourceOrderIds[index];
				if (!placed.Contains(id))
				{
					playlist.TrackIds.Add(id);
					placed.Add(id);
				}
			}
			for (int index = 0; index < playlist.TrackIds.Count; index++)
			{
				totalDuration = totalDuration + durationsById[playlist.TrackIds[index]];
			}

			playlist.DurationSeconds = totalDuration;
			m_database.CreateOrUpdate(playlist);
			SaveDB("playlist-import");

			//Console.WriteLine("Pulse: Imported playlist \"" + name + "\": " + matched + " matched, " + missed + " missed, " + entries.Count + " total");
			return playlist;
		}

		private string NormalizeForMatch(string input)
		{
			string result = input.ToLowerInvariant();
			result = result.Replace("&", "and");
			if (result.StartsWith("the "))
			{
				result = result.Substring(4);
			}
			return result.Trim();
		}

		private string NormalizeTitle(string input, string normalizedArtist)
		{
			string result = input.ToLowerInvariant();
			// Strip parenthetical suffixes: (Remastered), (feat. X), (Deluxe), (Live), (Bonus Track), etc.
			int parenStart = result.IndexOf('(');
			if (parenStart > 0)
			{
				string inside = result.Substring(parenStart).ToLowerInvariant();
				if (inside.Contains("remaster") || inside.Contains("feat") || inside.Contains("ft.")
					|| inside.Contains("deluxe") || inside.Contains("live") || inside.Contains("bonus")
					|| inside.Contains("radio") || inside.Contains("edit") || inside.Contains("version")
					|| inside.Contains("mono") || inside.Contains("stereo") || inside.Contains("acoustic")
					|| inside.Contains("remix") || inside.Contains("single"))
				{
					result = result.Substring(0, parenStart);
				}
			}
			// Strip dash suffixes: - Remastered 2011, - Live, - Bonus Track, etc.
			int dashIndex = result.IndexOf(" - ");
			if (dashIndex > 0)
			{
				string after = result.Substring(dashIndex + 3).Trim();
				if (after.StartsWith("remaster") || after.StartsWith("live") || after.StartsWith("bonus")
					|| after.StartsWith("deluxe") || after.StartsWith("feat") || after.StartsWith("ft.")
					|| after.StartsWith("radio") || after.StartsWith("edit") || after.StartsWith("version")
					|| after.StartsWith("mono") || after.StartsWith("stereo") || after.StartsWith("acoustic")
					|| after.StartsWith("remix") || after.StartsWith("single") || after.StartsWith("from"))
				{
					result = result.Substring(0, dashIndex);
				}
				else if (after.Length > 0 && char.IsDigit(after[0]))
				{
					result = result.Substring(0, dashIndex);
				}
			}
			result = result.Replace("&", "and");

			// Strip composer prefix from title (e.g. "Chopin: Nocturne..." -> "Nocturne...")
			int colonIndex = result.IndexOf(": ");
			if (colonIndex > 0 && colonIndex < 30)
			{
				string beforeColon = result.Substring(0, colonIndex);
				// Only strip if the prefix looks like it matches the artist (avoid stripping legitimate colons)
				if (normalizedArtist.Contains(beforeColon) || beforeColon.Contains(normalizedArtist))
				{
					result = result.Substring(colonIndex + 2);
				}
			}

			return result.Trim();
		}

		private List<string> SplitArtists(string normalizedArtist)
		{
			List<string> parts = new List<string>();
			string[] delimiters = new string[] { ",", " and ", " & ", " feat ", " feat. ", " ft. ", " ft ", " with ", " x " };
			string[] splits = normalizedArtist.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
			for (int index = 0; index < splits.Length; index++)
			{
				string part = splits[index].Trim();
				if (part.Length > 0)
				{
					parts.Add(part);
				}
			}
			return parts;
		}

		private bool ArtistsOverlap(List<string> partsA, List<string> partsB)
		{
			for (int indexA = 0; indexA < partsA.Count; indexA++)
			{
				for (int indexB = 0; indexB < partsB.Count; indexB++)
				{
					if (partsA[indexA].Contains(partsB[indexB]) || partsB[indexB].Contains(partsA[indexA]))
					{
						return true;
					}
				}
			}
			return false;
		}

		public void CreateOrUpdatePlaylist(PlaylistInfo playlist)
		{
			m_database.CreateOrUpdate(playlist);
			SaveDB("playlist-create-update");
		}

		public void DeletePlaylist(string playlistId)
		{
			m_database.DeletePlaylist(playlistId);
			SaveDB("playlist-delete");
		}



		private void LoadDB()
		{
			// Environment selection: config drives in normal operation (Flatline
			// bug #67 -- behavior shouldn't change based on launch method) BUT a
			// debugger attached is a hard safety lockout to Staging. Debug
			// sessions must never touch production data -- a test interaction
			// scrobbling against the real DB is catastrophic and the silent
			// inverse (prod accidentally writes to staging) is recoverable.
			string environmentName = m_config.DatabaseEnvironment;
			if (string.IsNullOrWhiteSpace(environmentName))
			{
				environmentName = "Production";
			}
#if DEBUG
			//Enforce debug builds never touch production
			if (!string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase))
			{
				Log.Warning(-1, "Debugger attached: forcing Staging environment (config said '" + environmentName + "'). Debug sessions never touch production data.");
			}
			environmentName = "Staging";
#endif

			string pulseDataRoot = Path.Combine(m_config.MusicPath, "PulseData");
			if (!Directory.Exists(pulseDataRoot))
			{
				Directory.CreateDirectory(pulseDataRoot);
			}

			// Separate sqlite file per environment. Production -> pulse_production.db,
			// Staging -> pulse_staging.db. Keeps the existing concept while letting
			// the two run side-by-side without cross-contamination.
			string sqliteFileName = "pulse_" + environmentName.ToLowerInvariant() + ".db";
			string sqlitePath = Path.Combine(pulseDataRoot, sqliteFileName);
			Pulse.Database.SqliteConnectionFactory.SetDatabaseFilePath(sqlitePath);
			Pulse.Database.Migrations.RunMigrations();
			Log.Info(-1, "Pulse DB: env=" + environmentName + " path=" + sqlitePath);

			PulseSqliteDatabase sqliteDb = new PulseSqliteDatabase();
			m_database = sqliteDb;
			sqliteDb.Load();
		}

		private void SaveDB(string reason)
		{
			m_database.Save(reason);

			JsonSerializerOptions options = new JsonSerializerOptions
			{
				WriteIndented = true
			};
			lock (m_missingLock)
			{
				string missingTempPath = "missingSongs.json.tmp";
				string missingSongJson = JsonSerializer.Serialize(m_missingSongs, options);
				File.WriteAllText(missingTempPath, missingSongJson);
				File.Move(missingTempPath, "missingSongs.json", overwrite: true);
			}
		}

		private void RunScan(string musicPath)
		{
			int fileCount = 0;
			int processedCount = 0;
			int skippedCount = 0;

			string[] extensions = new string[] { ".mp3", ".flac", ".ogg", ".m4a", ".wma", ".wav" };

			List<TrackInfo> allTracks = m_database.GetAllTracks();

			HashSet<string> existingIds = new HashSet<string>();
			for(int i = 0; i<allTracks.Count;i++)
				existingIds.Add(allTracks[i].Id);

			m_scanningAlbumCache.Clear();
			m_scanningArtistCache.Clear();
			foreach (string filePath in Directory.EnumerateFiles(musicPath, "*.*", SearchOption.AllDirectories))
			{
				fileCount++;
				string libraryID = MusicManager.GenerateID(filePath);

			

				string extension = Path.GetExtension(filePath).ToLowerInvariant();
				bool supported = false;
				for (int extIndex = 0; extIndex < extensions.Length; extIndex++)
				{
					if (extension == extensions[extIndex])
					{
						supported = true;
						break;
					}
				}

				if (!supported)
				{
					continue;
				}

				if (existingIds.Contains(libraryID))
				{
					skippedCount++;
					continue;
				}

				try
				{
					processedCount++;
					ProcessFile(filePath, musicPath);
				}
				catch (Exception exception)
				{
					Log.Error(-1, "Scan failed: " + filePath + " - " + exception.Message);
				}
			}


			//remove missing files
			Log.Info(-1, "Scanning for deleted tracks...");
			for (int index = 0; index < allTracks.Count; index++)
			{
				if (!File.Exists(allTracks[index].FilePath))
				{
					string artistId = allTracks[index].ArtistId;
					m_database.RemoveTrack(allTracks[index].Id);
					ArtistInfo artist = m_database.GetArtist(artistId);
					if (artist != null)
					{
						artist.m_bIsDirty = true;
					}
					Log.Info(-1, "Pulse: Removed dead track: " + allTracks[index].FilePath);
				}
			}



			Log.Info(-1, "Pulse: Enumerated " + fileCount + " files, processed " + processedCount + ", skipped " + skippedCount + ", tracks in library " + m_database.GetTrackCount());
			m_scanning = false;
			SaveDB("scan-complete");
		}

		private void ProcessFile(string filePath, string musicRoot)
		{
			TagLib.File tagFile = TagLib.File.Create(filePath);

			string artist = tagFile.Tag.FirstAlbumArtist;
			if (string.IsNullOrEmpty(artist))
			{
				artist = tagFile.Tag.FirstPerformer;
			}
			if (string.IsNullOrEmpty(artist))
			{
				string relativePath = Path.GetRelativePath(musicRoot, filePath);
				string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
				if (parts.Length > 1)
				{
					artist = parts[0];
				}
				else
				{
					artist = "Unknown Artist";
				}
			}

			string album = tagFile.Tag.Album;
			if (string.IsNullOrEmpty(album))
			{
				string relativePath = Path.GetRelativePath(musicRoot, filePath);
				string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
				if (parts.Length > 2)
				{
					album = parts[1];
				}
				else
				{
					album = "Unknown Album";
				}
			}

			string title = tagFile.Tag.Title;
			if (string.IsNullOrEmpty(title))
			{
				title = Path.GetFileNameWithoutExtension(filePath);
			}

			string artistId = MusicManager.GenerateID(artist);
			ArtistInfo artistInfo = null;
			if (!m_scanningArtistCache.TryGetValue(artistId, out artistInfo))
			{
				artistInfo = m_database.GetOrCreateArtist(artistId, artist);
				m_scanningArtistCache[artistId] = artistInfo;
			}
			

			string albumId = MusicManager.GenerateID(artist + "/" + album);
			AlbumInfo albumInfo = null;
			if (!m_scanningAlbumCache.TryGetValue(artistId, out albumInfo))
			{
				albumInfo = m_database.GetOrCreateAlbum(albumId, album, artistId, artist, (int)tagFile.Tag.Year, tagFile.Tag.FirstGenre ?? "");
				m_scanningAlbumCache[artistId] = albumInfo;
			}
			

			string extension = Path.GetExtension(filePath).ToLowerInvariant();
			FileInfo fileInfo = new FileInfo(filePath);

			TrackInfo track = new TrackInfo();
			track.Id = MusicManager.GenerateID(filePath);
			track.Title = title;
			track.Artist = artist;
			track.ArtistId = artistId;
			track.Album = album;
			track.AlbumId = albumId;
			track.Genre = tagFile.Tag.FirstGenre ?? "";
			track.FilePath = filePath;
			track.CoverArtId = albumId;
			track.TrackNumber = (int)tagFile.Tag.Track;
			track.DiscNumber = (int)tagFile.Tag.Disc;
			track.Year = (int)tagFile.Tag.Year;
			track.DurationSeconds = (int)tagFile.Properties.Duration.TotalSeconds;
			track.FileSizeBytes = fileInfo.Length;
			track.Suffix = extension.TrimStart('.');
			track.ContentType = GetContentType(extension);

			m_database.AddTrack(track, albumId);
			tagFile.Dispose();

			int count = Interlocked.Increment(ref m_processedSinceLastSave);
			if (count % 500 == 0)
			{
				SaveDB("scan-batch");
			}
		}

		private void DebugDuplicates()
		{
			int duplicateAlbumTracks = 0;
			int duplicateArtistAlbums = 0;

			List<AlbumInfo> albums = m_database.GetAllAlbums();
			foreach (AlbumInfo album in albums)
			{
				HashSet<string> seenTrackIds = new HashSet<string>();
				for (int index = 0; index < album.Tracks.Count; index++)
				{
					if (!seenTrackIds.Add(album.Tracks[index].Id))
					{
						Log.Warning(-1, "Duplicate track in album \"" + album.Name + "\": " + album.Tracks[index].Title + " (" + album.Tracks[index].Id + ")");
						duplicateAlbumTracks++;
					}
				}
			}

			List<ArtistInfo> artists = m_database.GetAllArtists();
			foreach (ArtistInfo artist in artists)
			{
				HashSet<string> seenAlbumIds = new HashSet<string>();
				for (int index = 0; index < artist.Albums.Count; index++)
				{
					if (!seenAlbumIds.Add(artist.Albums[index].Id))
					{
						Log.Warning(-1, "Duplicate album in artist \"" + artist.Name + "\": " + artist.Albums[index].Name + " (" + artist.Albums[index].Id + ")");
						duplicateArtistAlbums++;
					}
				}
			}

			int totalTracksInAlbums = 0;
			foreach (AlbumInfo album in albums)
			{
				totalTracksInAlbums = totalTracksInAlbums + album.Tracks.Count;
			}

			Log.Info(-1, "Pulse: Tracks in dictionary: " + m_database.GetTrackCount() + ", tracks across albums: " + totalTracksInAlbums + ", duplicate tracks: " + duplicateAlbumTracks + ", duplicate albums: " + duplicateArtistAlbums);
		}

		public void RecalculateScore(TrackInfo track)
		{
			float trackDuration = track.DurationSeconds;
			if (trackDuration == 0)
			{
				trackDuration = 5 * 60; ///use a 5 minute track as a best guess
			}
			if (track.Score.PlayCount > 0 && track.DurationSeconds > 0)
			{
				float averageListenRatio = (float)(track.Score.TotalListenSeconds / (track.Score.PlayCount * trackDuration));
				float confidence = (float)track.Score.PlayCount / (track.Score.PlayCount + 5);
				track.Score.WeightedScore = (averageListenRatio * confidence) + (0.5f * (1f - confidence));
				if (track.Score.WeightedScore > 1)
				{
					track.Score.WeightedScore = 1;
				}
			}

			foreach (string user in track.UserScore.Keys)
			{
				if (track.UserScore[user].PlayCount == 0)
				{
					track.UserScore[user].WeightedScore = 0f;
					continue;
				}
				float userListenRatio = (float)(track.UserScore[user].TotalListenSeconds / (track.UserScore[user].PlayCount * trackDuration));
				float userConfidence = (float)track.UserScore[user].PlayCount / (track.UserScore[user].PlayCount + 5);
				track.UserScore[user].WeightedScore = (userListenRatio * userConfidence) + (0.5f * (1f - userConfidence));
				if (track.UserScore[user].WeightedScore > 1)
				{
					track.UserScore[user].WeightedScore = 1;
				}
			}
			track.m_bIsDirty = true;
		}

		private string GetContentType(string extension)
		{
			switch (extension)
			{
				case ".mp3": return "audio/mpeg";
				case ".flac": return "audio/flac";
				case ".ogg": return "audio/ogg";
				case ".m4a": return "audio/mp4";
				case ".wma": return "audio/x-ms-wma";
				case ".wav": return "audio/wav";
				default: return "application/octet-stream";
			}
		}

		private int LevenshteinDistance(string source, string target)
		{
			if (source.Length == 0)
			{
				return target.Length;
			}
			if (target.Length == 0)
			{
				return source.Length;
			}

			int sourceLength = source.Length;
			int targetLength = target.Length;
			int[,] matrix = new int[sourceLength + 1, targetLength + 1];

			for (int row = 0; row <= sourceLength; row++)
			{
				matrix[row, 0] = row;
			}
			for (int col = 0; col <= targetLength; col++)
			{
				matrix[0, col] = col;
			}

			for (int row = 1; row <= sourceLength; row++)
			{
				for (int col = 1; col <= targetLength; col++)
				{
					int cost;
					if (source[row - 1] == target[col - 1])
					{
						cost = 0;
					}
					else
					{
						cost = 1;
					}
					int deleteCost = matrix[row - 1, col] + 1;
					int insertCost = matrix[row, col - 1] + 1;
					int replaceCost = matrix[row - 1, col - 1] + cost;

					int minimum = deleteCost;
					if (insertCost < minimum)
					{
						minimum = insertCost;
					}
					if (replaceCost < minimum)
					{
						minimum = replaceCost;
					}

					matrix[row, col] = minimum;
				}
			}

			return matrix[sourceLength, targetLength];
		}
	}
}