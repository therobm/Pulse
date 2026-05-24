using Pulse.Data;
using Pulse.Lidarr;
using System.Diagnostics.Contracts;
using System.Text.Json;

namespace Pulse.MusicLibrary
{
	public class PlaylistImportEntry
	{
		public string Artist { get; set; }
		public string Title { get; set; }
	}

	public class MusicManager
	{
		public int TrackCount { get { return m_db.TrackCount; } }
		public int AlbumCount { get { return m_db.AlbumCount; } }
		public int ArtistCount { get { return m_db.ArtistCount; } }
		
		public bool IsScanning { get { return m_scanning; } }
		public IPulseDatabase Db { get { return m_db; } }

		private IPulseDatabase m_db;
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

		public void Run(string musicPath, string cachePath = null)
		{
			if (m_scanning)
			{
				return;
			}

			LoadDB();
			DebugDuplicates();
			m_scanThread = new Thread(() => RunScan(musicPath));
			m_scanThread.IsBackground = true;
			m_scanThread.Name = "Pulse.MusicScan";
			m_scanning = true;
			m_scanThread.Start();
		}

		public void OnPlaylistSyncComplete()
		{
			m_lidarrSync.RequestArtists(m_missingSongs);
		}

		public void OnTrackStreamed(string userName, string trackId)
		{
			if (m_nowPlayingTrackId == trackId)
				return;

			if (m_nowPlayingTrackId != null && m_nowPlayingTrackId != trackId)
			{
				TrackInfo previousTrack = m_db.GetTrack(m_nowPlayingTrackId);
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

					PulseAnalyticsInfo analytics = m_db.Analytics;
					int artistCount = 0;
					analytics.ArtistPlayCounts.TryGetValue(previousTrack.ArtistId, out artistCount);
					analytics.ArtistPlayCounts[previousTrack.ArtistId] = artistCount + 1;
					analytics.RecentlyPlayed.Remove(m_nowPlayingTrackId);
					analytics.RecentlyPlayed.Insert(0, m_nowPlayingTrackId);
					if (analytics.RecentlyPlayed.Count > 50)
					{
						analytics.RecentlyPlayed.RemoveAt(analytics.RecentlyPlayed.Count - 1);
					}
					analytics.m_bIsDirty = true;

					RecalculateScore(previousTrack);

					bool skipped = elapsedSeconds < threshold;
					Log.Info(-1, "Finalized: " + previousTrack.Artist + ":" + previousTrack.Title
						+ " elapsed=" + elapsedSeconds.ToString("F1") + "s"
						+ " duration=" + previousTrack.DurationSeconds + "s"
						+ " listen=" + listenSeconds.ToString("F1") + "s"
						+ " plays=" + previousTrack.Score.PlayCount
						+ " skips=" + previousTrack.Score.SkipCount
						+ " score=" + previousTrack.Score.WeightedScore.ToString("F3")
						+ (skipped ? " SKIPPED" : ""));

					SaveDB();
				}
			}

			m_nowPlayingTrackId = trackId;
			m_nowPlayingStartTime = DateTime.UtcNow;

			TrackInfo newTrack = m_db.GetTrack(m_nowPlayingTrackId);
			if (newTrack != null)
			{
				string user = "na";
				if (!string.IsNullOrEmpty(userName))
					user = userName;
				Log.Info(-1, "[" + user + "] Streaming: " + newTrack.Artist + ":" + newTrack.Title);
			}
		}

		public PlaylistInfo ImportPlaylist(string name, List<PlaylistImportEntry> entries)
		{
			//Console.WriteLine("Importing playlist: " + name);
			PlaylistInfo playlist = new PlaylistInfo();
			playlist.Id = MusicManager.GenerateID("playlist/" + name);
			playlist.Name = name;

			int matched = 0;
			int missed = 0;
			long totalDuration = 0;

			List<TrackInfo> tracks = m_db.GetAllTracks();
			string[] normalizedTrackArtists = new string[tracks.Count];
			string[] normalizedTrackTitles = new string[tracks.Count];
			List<string>[] splitTrackArtists = new List<string>[tracks.Count];
			for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
			{
				normalizedTrackArtists[trackIndex] = NormalizeForMatch(tracks[trackIndex].Artist);
				normalizedTrackTitles[trackIndex] = NormalizeTitle(tracks[trackIndex].Title, normalizedTrackArtists[trackIndex]);
				splitTrackArtists[trackIndex] = SplitArtists(normalizedTrackArtists[trackIndex]);
			}



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
					playlist.TrackIds.Add(bestMatch.Id);
					totalDuration = totalDuration + bestMatch.DurationSeconds;
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
					m_missingSongs.Add(entries[index].Artist + " - " + entries[index].Title);
					missed++;
				}
			}

			playlist.DurationSeconds = totalDuration;
			m_db.CreateOrUpdate(playlist);
			SaveDB();

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
			m_db.CreateOrUpdate(playlist);
			SaveDB();
		}

		public void DeletePlaylist(string playlistId)
		{
			m_db.DeletePlaylist(playlistId);
			SaveDB();
		}



		private void LoadDB()
		{
			string dbPath = Path.Combine(m_config.MusicPath, "PulseData/Staging");
			if (!System.Diagnostics.Debugger.IsAttached)
				dbPath = Path.Combine(m_config.MusicPath, "PulseData/Production");

			if (!Directory.Exists(dbPath))
				Directory.CreateDirectory(dbPath);

			PulseFileDatabase fileDB = new PulseFileDatabase(dbPath, this);
			m_db = fileDB;
			fileDB.Load();
		}

		private void SaveDB()
		{
			m_db.Save();

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

			foreach (string filePath in Directory.EnumerateFiles(musicPath, "*.*", SearchOption.AllDirectories))
			{
				fileCount++;
				string libraryID = MusicManager.GenerateID(filePath);

				if (m_db.TrackExists(libraryID))
				{
					skippedCount++;
					continue;
				}

				string ext = Path.GetExtension(filePath).ToLowerInvariant();

				bool supported = false;
				for (int extIndex = 0; extIndex < extensions.Length; extIndex++)
				{
					if (ext == extensions[extIndex])
					{
						supported = true;
						break;
					}
				}
				if (!supported)
				{
					continue;
				}

				try
				{
					processedCount++;
					ProcessFile(filePath, musicPath);
				}
				catch (Exception exception)
				{
					Console.WriteLine("Scan failed: " + filePath + " - " + exception.Message);
				}
			}


			//remove missing files
			Console.WriteLine("Scanning for deleted tracks...");
			List<TrackInfo> allTracks = m_db.GetAllTracks();
			for (int index = 0; index < allTracks.Count; index++)
			{
				if (!File.Exists(allTracks[index].FilePath))
				{
					string artistId = allTracks[index].ArtistId;
					m_db.RemoveTrack(allTracks[index].Id);
					ArtistInfo artist = m_db.GetArtist(artistId);
					if (artist != null)
					{
						artist.m_bIsDirty = true;
					}
					Console.WriteLine("Pulse: Removed dead track: " + allTracks[index].FilePath);
				}
			}



			Console.WriteLine("Pulse: Enumerated " + fileCount + " files, processed " + processedCount + ", skipped " + skippedCount + ", tracks in library " + m_db.TrackCount);
			m_scanning = false;
			SaveDB();
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
				artist = parts.Length > 1 ? parts[0] : "Unknown Artist";
			}

			string album = tagFile.Tag.Album;
			if (string.IsNullOrEmpty(album))
			{
				string relativePath = Path.GetRelativePath(musicRoot, filePath);
				string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
				album = parts.Length > 2 ? parts[1] : "Unknown Album";
			}

			string title = tagFile.Tag.Title;
			if (string.IsNullOrEmpty(title))
			{
				title = Path.GetFileNameWithoutExtension(filePath);
			}

			string artistId = MusicManager.GenerateID(artist);
			ArtistInfo artistInfo = m_db.GetOrCreateArtist(artistId, artist);

			string albumId = MusicManager.GenerateID(artist + "/" + album);
			AlbumInfo albumInfo = m_db.GetOrCreateAlbum(albumId, album, artistId, artist, (int)tagFile.Tag.Year, tagFile.Tag.FirstGenre ?? "");

			string ext = Path.GetExtension(filePath).ToLowerInvariant();
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
			track.Suffix = ext.TrimStart('.');
			track.ContentType = GetContentType(ext);

			m_db.AddTrack(track, albumId);
			tagFile.Dispose();

			int count = Interlocked.Increment(ref m_processedSinceLastSave);
			if (count % 500 == 0)
			{
				SaveDB();
			}
		}

		private void DebugDuplicates()
		{
			int duplicateAlbumTracks = 0;
			int duplicateArtistAlbums = 0;

			List<AlbumInfo> albums = m_db.GetAllAlbums();
			foreach (AlbumInfo album in albums)
			{
				HashSet<string> seenTrackIds = new HashSet<string>();
				for (int index = 0; index < album.Tracks.Count; index++)
				{
					if (!seenTrackIds.Add(album.Tracks[index].Id))
					{
						Console.WriteLine("Duplicate track in album \"" + album.Name + "\": " + album.Tracks[index].Title + " (" + album.Tracks[index].Id + ")");
						duplicateAlbumTracks++;
					}
				}
			}

			List<ArtistInfo> artists = m_db.GetAllArtists();
			foreach (ArtistInfo artist in artists)
			{
				HashSet<string> seenAlbumIds = new HashSet<string>();
				for (int index = 0; index < artist.Albums.Count; index++)
				{
					if (!seenAlbumIds.Add(artist.Albums[index].Id))
					{
						Console.WriteLine("Duplicate album in artist \"" + artist.Name + "\": " + artist.Albums[index].Name + " (" + artist.Albums[index].Id + ")");
						duplicateArtistAlbums++;
					}
				}
			}

			int totalTracksInAlbums = 0;
			foreach (AlbumInfo album in albums)
			{
				totalTracksInAlbums = totalTracksInAlbums + album.Tracks.Count;
			}

			Console.WriteLine("Pulse: Tracks in dictionary: " + m_db.TrackCount + ", tracks across albums: " + totalTracksInAlbums + ", duplicate tracks: " + duplicateAlbumTracks + ", duplicate albums: " + duplicateArtistAlbums);
		}

		public void RecalculateScore(TrackInfo track)
		{
			float trackDuration = track.DurationSeconds;
			if (trackDuration == 0)
				trackDuration = 5 * 60; ///use a 5 minute track as a best guess
			if (track.Score.PlayCount > 0 && track.DurationSeconds > 0)
			{
				float averageListenRatio = (float)(track.Score.TotalListenSeconds / (track.Score.PlayCount * trackDuration));
				float confidence = (float)track.Score.PlayCount / (track.Score.PlayCount + 5);
				track.Score.WeightedScore = (averageListenRatio * confidence) + (0.5f * (1f - confidence));
				if (track.Score.WeightedScore > 1)
					track.Score.WeightedScore = 1;
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
					track.UserScore[user].WeightedScore = 1;
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
			if (source.Length == 0) return target.Length;
			if (target.Length == 0) return source.Length;

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
					int cost = source[row - 1] == target[col - 1] ? 0 : 1;
					int deleteCost = matrix[row - 1, col] + 1;
					int insertCost = matrix[row, col - 1] + 1;
					int replaceCost = matrix[row - 1, col - 1] + cost;

					int minimum = deleteCost;
					if (insertCost < minimum) minimum = insertCost;
					if (replaceCost < minimum) minimum = replaceCost;

					matrix[row, col] = minimum;
				}
			}

			return matrix[sourceLength, targetLength];
		}
	}
}