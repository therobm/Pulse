using Pulse.Database;
using Pulse.DataStorage;
using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Pulse.Data
{
	public class PulseData
	{
		private ConcurrentDictionary<string, TrackData> m_tracks = new ConcurrentDictionary<string, TrackData>();
		private ConcurrentDictionary<string, AlbumData> m_albums = new ConcurrentDictionary<string, AlbumData>();
		private ConcurrentDictionary<string, ArtistData> m_artists = new ConcurrentDictionary<string, ArtistData>();
		private ConcurrentDictionary<string, PlaylistData> m_playlists = new ConcurrentDictionary<string, PlaylistData>();
		private PulseAnalyticsData m_analytics = new PulseAnalyticsData();

		[Obsolete]
		private PulseDB m_db = new PulseDB();
		private PulseDataStore m_musicData;
		private UserData m_userData;
		private Timer m_saveTimer;

		private PulseConfig m_config;
		public PulseData(PulseConfig config)
		{
			m_config = config;

			string musicDB = "music.db";
#if DEBUG
			musicDB = "music_staging.db";
#endif
			string dbPath = Path.Combine(m_config.PulseDataPath, musicDB);
			m_musicData = new PulseDataStore(dbPath);

			m_userData = new UserData(config);
		}

		public int GetTrackCount()
		{
			return m_tracks.Count;
		}
		public int GetAlbumCount()
		{
			return m_albums.Count;
		}
		public int GetArtistCount()
		{
			return m_artists.Count;
		}
		public PulseAnalyticsData GetAnalytics()
		{
			return m_analytics;
		}

		public TrackData GetTrack(string id)
		{
			TrackData track;
			m_tracks.TryGetValue(id, out track);
			return track;
		}

		public AlbumData GetAlbum(string id)
		{
			AlbumData album;
			m_albums.TryGetValue(id, out album);
			return album;
		}

		public ArtistData GetArtist(string id)
		{
			ArtistData artist;
			m_artists.TryGetValue(id, out artist);
			return artist;
		}

		public List<TrackData> GetAllTracks()
		{
			return new List<TrackData>(m_tracks.Values);
		}

		public List<AlbumData> GetAllAlbums()
		{
			return new List<AlbumData>(m_albums.Values);
		}

		private static int CompareArtistByName(ArtistData left, ArtistData right)
		{
			return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
		}

		public List<ArtistData> GetAllArtists()
		{
			List<ArtistData> list = new List<ArtistData>(m_artists.Values);
			list.Sort(CompareArtistByName);
			return list;
		}

		public bool TrackExists(string trackId)
		{
			return m_tracks.ContainsKey(trackId);
		}

		
		public void UpdateStar(string userName, string trackId, string albumId, string artistId, bool starred)
		{
			if (!string.IsNullOrEmpty(trackId))
			{
				TrackData track;
				if (m_tracks.TryGetValue(trackId, out track))
				{
					track.Starred[userName] = starred;
					track.MarkDirty();
				}
			}

			if (!string.IsNullOrEmpty(albumId))
			{
				AlbumData album;
				if (m_albums.TryGetValue(albumId, out album))
				{
					album.Starred[userName] = starred;
					album.MarkDirty();
				}
			}

			if (!string.IsNullOrEmpty(artistId))
			{
				ArtistData artist;
				if (m_artists.TryGetValue(artistId, out artist))
				{
					artist.Starred[userName] = starred;
					artist.MarkDirty();
				}
			}
		}

		public PlaylistData GetPlaylist(string id)
		{
			PlaylistData playlist;
			if (m_playlists.TryGetValue(id, out playlist))
			{
				return playlist;
			}
			return null;
		}

		public List<PlaylistData> GetAllPlaylists(string userName)
		{
			return new List<PlaylistData>(m_playlists.Values);
		}
		public List<PlaylistData> GetGenericPlaylists()
		{
			return new List<PlaylistData>(m_playlists.Values);
		}
		public List<TrackData> GetPlaylistTracks(string playlistId)
		{
			PlaylistData playlist;
			if (!m_playlists.TryGetValue(playlistId, out playlist))
			{
				return new List<TrackData>();
			}

			List<TrackData> tracks = new List<TrackData>();
			for (int index = 0; index < playlist.TrackIds.Count; index++)
			{
				TrackData track;
				if (m_tracks.TryGetValue(playlist.TrackIds[index], out track))
				{
					tracks.Add(track);
				}
			}
			return tracks;
		}

		public void DeletePlaylist(string playlistId)
		{
			m_playlists.TryRemove(playlistId, out _);
			m_musicData.Delete(eDataType.Playlist, playlistId);
		}

		public void CreateOrUpdate(PlaylistData playlist)
		{
			m_playlists[playlist.Id] = playlist;
			m_playlists[playlist.Id].MarkDirty();
		}

		public void CreateOrUpdate(ArtistData ArtistData)
		{
			ArtistData artist;
			if (!m_artists.TryGetValue(ArtistData.Id, out artist))
			{
				artist = new ArtistData();
				m_artists[ArtistData.Id] = artist;
			}
			artist.Id = ArtistData.Id;
			artist.Name = ArtistData.Name;
			artist.MarkDirty();
		}

		public ArtistData GetOrCreateArtist(string id, string name)
		{
			ArtistData artist;
			if (m_artists.TryGetValue(id, out artist))
			{
				return artist;
			}

			artist = new ArtistData();
			artist.Id = id;
			artist.Name = name;
			artist.MarkDirty();
			m_artists[id] = artist;
			return artist;
		}

		public AlbumData GetOrCreateAlbum(string id, string name, string artistId, string artistName, int year, string genre)
		{
			AlbumData album;
			if (m_albums.TryGetValue(id, out album))
			{
				return album;
			}

			ArtistData artist = GetArtist(artistId);

			album = new AlbumData();
			album.Id = id;
			album.Name = name;
			album.ArtistId = artistId;
			album.ArtistName = artistName;
			album.Year = year;
			album.Genre = genre;
			album.CoverArtId = id;
			m_albums[id] = album;

			if (artist != null)
			{
				artist.Albums.Add(album);
				artist.MarkDirty();
			}

			return album;
		}

		public void AddTrack(TrackData track, string albumId)
		{
			m_tracks[track.Id] = track;
			track.MarkDirty();

			AlbumData album;
			if (m_albums.TryGetValue(albumId, out album))
			{
				album.Tracks.Add(track);
				album.MarkDirty();
			}
		}

		// Capture the parent ids before the in-memory cascade mutates the maps.
		// The cascade removes the album from m_albums when its last track goes,
		// and the artist from m_artists when its last album goes -- mirror that
		// into SQL so orphaned album/artist rows (and their starred rows) don't
		// survive and reappear on the next Load (#302).
		public bool RemoveTrack(string trackId)
		{
			string albumId = null;
			string artistId = null;
			TrackData existing;
			if (m_tracks.TryGetValue(trackId, out existing))
			{
				albumId = existing.AlbumId;
				artistId = existing.ArtistId;
			}

			if (!RemoveTrackInMemory(trackId))
			{
				return false;
			}

			bool albumEmptied = !string.IsNullOrEmpty(albumId) && !m_albums.ContainsKey(albumId);
			bool artistEmptied = !string.IsNullOrEmpty(artistId) && !m_artists.ContainsKey(artistId);

			m_musicData.Delete(eDataType.Track, trackId);
			if (albumEmptied)
				m_musicData.Delete(eDataType.Album, albumId);
			if (artistEmptied)
				m_musicData.Delete(eDataType.Artist, artistId);


			return true;
		}

		private bool RemoveTrackInMemory(string trackId)
		{
			TrackData track;
			if (!m_tracks.TryRemove(trackId, out track))
			{
				return false;
			}

			AlbumData album;
			if (m_albums.TryGetValue(track.AlbumId, out album))
			{
				for (int trackIndex = album.Tracks.Count - 1; trackIndex >= 0; trackIndex--)
				{
					if (album.Tracks[trackIndex].Id == trackId)
					{
						album.Tracks.RemoveAt(trackIndex);
					}
				}

				if (album.Tracks.Count == 0)
				{
					m_albums.TryRemove(track.AlbumId, out _);

					ArtistData artist;
					if (m_artists.TryGetValue(track.ArtistId, out artist))
					{
						for (int albumIndex = artist.Albums.Count - 1; albumIndex >= 0; albumIndex--)
						{
							if (artist.Albums[albumIndex].Id == track.AlbumId)
							{
								artist.Albums.RemoveAt(albumIndex);
							}
						}

						if (artist.Albums.Count == 0)
						{
							m_artists.TryRemove(track.ArtistId, out _);
						}
					}
				}
			}
			return true;
		}

		public void Load()
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
				Log.Warning("Debugger attached: forcing Staging environment (config said '" + environmentName + "'). Debug sessions never touch production data.");
			}
			environmentName = "Staging";
#endif

			if (!Directory.Exists(m_config.PulseDataPath))
			{
				Directory.CreateDirectory(m_config.PulseDataPath);
			}

			// Separate sqlite file per environment. Production -> pulse_production.db,
			// Staging -> pulse_staging.db. Keeps the existing concept while letting
			// the two run side-by-side without cross-contamination.
			string sqliteFileName = "pulse_" + environmentName.ToLowerInvariant() + ".db";
			string sqlitePath = Path.Combine(m_config.PulseDataPath, sqliteFileName);
			PulseDBConnector.SetDatabaseFilePath(sqlitePath);
			PulseDBMigrations.RunMigrations();
			m_userData.Load();
			Log.Info("Pulse DB: env=" + environmentName + " path=" + sqlitePath);


			Stopwatch sw = Stopwatch.StartNew();

			// PulseDataStore is the source of truth for domain objects.
			// Fall back to PulseDB for one-time migration on first run.
			List<TrackData> tracks = m_musicData.LoadList<TrackData>(eDataType.Track);
			if (tracks.Count > 0)
			{
				for (int i = 0; i < tracks.Count; i++)
				{
					m_tracks[tracks[i].Id] = tracks[i];
				}

				List<AlbumData> albums = m_musicData.LoadList<AlbumData>(eDataType.Album);
				for (int i = 0; i < albums.Count; i++)
				{
					if (albums[i].ArtistName.ToLower().Contains("apple"))
					{
						int asdf = 0;
					}
					m_albums[albums[i].Id] = albums[i];
				}

				List<ArtistData> artists = m_musicData.LoadList<ArtistData>(eDataType.Artist);
				for (int i = 0; i < artists.Count; i++)
				{
					m_artists[artists[i].Id] = artists[i];
				}

				List<PlaylistData> playlists = m_musicData.LoadList<PlaylistData>(eDataType.Playlist);
				for (int i = 0; i < playlists.Count; i++)
				{
					m_playlists[playlists[i].Id] = playlists[i];
				}

				PulseAnalyticsData analytics = m_musicData.Load<PulseAnalyticsData>(eDataType.PulseAnalytics, "analytics");
				if (analytics != null)
				{
					m_analytics = analytics;
				}
			}
			else
			{
				MigrateFromLegacyDB();
			}

			MigrateFields();
			WireUpReferences();
			CalculateArtistScores();

			sw.Stop();
			Log.Info("PulseData loaded in " + sw.ElapsedMilliseconds + "ms: "
				+ m_tracks.Count + " tracks, " + m_albums.Count + " albums, "
				+ m_artists.Count + " artists, " + m_playlists.Count + " playlists");

			m_saveTimer = new Timer(OnSaveTimer, null, 10000, 10000);
		}

		/// <summary>
		/// One-time migration from PulseDB normalized tables into PulseDataStore.
		/// Runs only when the PulseDataStore has no tracks (first boot after cutover).
		/// </summary>
		private void MigrateFromLegacyDB()
		{
			Log.Info("PulseDataStore empty — migrating from PulseDB");

			List<ArtistData> artists = m_db.LoadArtists();
			for (int i = 0; i < artists.Count; i++)
			{
				m_artists[artists[i].Id] = artists[i];
			}

			List<AlbumData> albums = m_db.LoadAlbums();
			for (int i = 0; i < albums.Count; i++)
			{
				m_albums[albums[i].Id] = albums[i];
			}

			List<TrackData> tracks = m_db.LoadTracks();
			for (int i = 0; i < tracks.Count; i++)
			{
				m_tracks[tracks[i].Id] = tracks[i];
			}

			List<TrackUserScoreRow> userScoreRows = m_db.LoadTrackUserScores();
			for (int i = 0; i < userScoreRows.Count; i++)
			{
				TrackUserScoreRow row = userScoreRows[i];
				TrackData track;
				if (m_tracks.TryGetValue(row.TrackId, out track))
				{
					track.UserScore[row.UserName] = row.Score;
				}
			}

			List<StarredRow> starredRows = m_db.LoadStarred();
			for (int i = 0; i < starredRows.Count; i++)
			{
				StarredRow row = starredRows[i];
				if (row.EntityKind == "track")
				{
					TrackData track;
					if (m_tracks.TryGetValue(row.EntityId, out track))
					{
						track.Starred[row.UserName] = row.Starred;
					}
				}
				else if (row.EntityKind == "album")
				{
					AlbumData album;
					if (m_albums.TryGetValue(row.EntityId, out album))
					{
						album.Starred[row.UserName] = row.Starred;
					}
				}
				else if (row.EntityKind == "artist")
				{
					ArtistData artist;
					if (m_artists.TryGetValue(row.EntityId, out artist))
					{
						artist.Starred[row.UserName] = row.Starred;
					}
				}
			}

			Dictionary<string, string> legacyToCurrentId = new Dictionary<string, string>();
			foreach (TrackData track in m_tracks.Values)
			{
				if (!string.IsNullOrEmpty(track.LegacyId) && track.LegacyId != track.Id)
				{
					legacyToCurrentId[track.LegacyId] = track.Id;
				}
			}

			List<PlaylistData> playlists = m_db.LoadPlaylists();
			for (int i = 0; i < playlists.Count; i++)
			{
				PlaylistData playlist = playlists[i];
				if (legacyToCurrentId.Count > 0)
				{
					for (int j = 0; j < playlist.TrackIds.Count; j++)
					{
						string currentId;
						if (legacyToCurrentId.TryGetValue(playlist.TrackIds[j], out currentId))
						{
							playlist.TrackIds[j] = currentId;
						}
					}
				}
				m_playlists[playlist.Id] = playlist;
			}

			List<string> recentlyPlayed = m_db.LoadRecentlyPlayed();
			for (int i = 0; i < recentlyPlayed.Count; i++)
			{
				m_analytics.RecentlyPlayed.Add(recentlyPlayed[i]);
			}

			// Bulk-write to PulseDataStore
			m_musicData.SaveList(eDataType.Track, new List<TrackData>(m_tracks.Values));
			m_musicData.SaveList(eDataType.Album, new List<AlbumData>(m_albums.Values));
			m_musicData.SaveList(eDataType.Artist, new List<ArtistData>(m_artists.Values));
			m_musicData.SaveList(eDataType.Playlist, new List<PlaylistData>(m_playlists.Values));
			m_musicData.Save(eDataType.PulseAnalytics, m_analytics);

			// Clear dirty flags — everything was just persisted
			foreach (TrackData track in m_tracks.Values) { track.ClearDirty(); }
			foreach (AlbumData album in m_albums.Values) { album.ClearDirty(); }
			foreach (ArtistData artist in m_artists.Values) { artist.ClearDirty(); }
			foreach (PlaylistData playlist in m_playlists.Values) { playlist.ClearDirty(); }
			m_analytics.ClearDirty();

			Log.Info("PulseDataStore migration complete");
		}

		/// <summary>
		/// Write all dirty domain objects to PulseDataStore and clear their flags.
		/// Called periodically by the save timer and on explicit flush.
		/// </summary>
		public void Save()
		{
			m_userData.Save();

			List<ArtistData> dirtyArtists = new List<ArtistData>();
			foreach (ArtistData artist in m_artists.Values)
			{
				if (artist.IsDirty()) { dirtyArtists.Add(artist); }
			}

			List<AlbumData> dirtyAlbums = new List<AlbumData>();
			foreach (AlbumData album in m_albums.Values)
			{
				if (album.IsDirty()) { dirtyAlbums.Add(album); }
			}

			List<TrackData> dirtyTracks = new List<TrackData>();
			foreach (TrackData track in m_tracks.Values)
			{
				if (track.IsDirty()) { dirtyTracks.Add(track); }
			}

			List<PlaylistData> dirtyPlaylists = new List<PlaylistData>();
			foreach (PlaylistData playlist in m_playlists.Values)
			{
				if (playlist.IsDirty()) { dirtyPlaylists.Add(playlist); }
			}

			if (dirtyArtists.Count > 0)
			{
				m_musicData.SaveList(eDataType.Artist, dirtyArtists);
				for (int i = 0; i < dirtyArtists.Count; i++) { dirtyArtists[i].ClearDirty(); }
			}

			if (dirtyAlbums.Count > 0)
			{
				m_musicData.SaveList(eDataType.Album, dirtyAlbums);
				for (int i = 0; i < dirtyAlbums.Count; i++) { dirtyAlbums[i].ClearDirty(); }
			}

			if (dirtyTracks.Count > 0)
			{
				m_musicData.SaveList(eDataType.Track, dirtyTracks);
				for (int i = 0; i < dirtyTracks.Count; i++) { dirtyTracks[i].ClearDirty(); }
			}

			if (dirtyPlaylists.Count > 0)
			{
				m_musicData.SaveList(eDataType.Playlist, dirtyPlaylists);
				for (int i = 0; i < dirtyPlaylists.Count; i++) { dirtyPlaylists[i].ClearDirty(); }
			}

			if (m_analytics.IsDirty())
			{
				m_musicData.Save(eDataType.PulseAnalytics, m_analytics);
				m_analytics.ClearDirty();
			}
		}

		private void OnSaveTimer(object state)
		{
			try
			{
				Save();
			}
			catch (Exception)
			{
			}
		}

		/// <summary>
		/// Stop the periodic save timer and flush any remaining dirty objects.
		/// Call once during process shutdown.
		/// </summary>
		public void Shutdown()
		{
			if (m_saveTimer != null)
			{
				m_saveTimer.Dispose();
				m_saveTimer = null;
			}
			try
			{
				Save();
				Log.Info("PulseData: shutdown flush complete");
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}

		private void MigrateFields()
		{
			List<string> tracksToRemove = new List<string>();
			List<TrackData> tracksToAdd = new List<TrackData>();
			foreach(KeyValuePair<string, TrackData> trackPair in m_tracks)
			{
				TrackData track = trackPair.Value;

				//add relative path
				if (string.IsNullOrEmpty(track.RelativeFilePath))
				{
					string absPath = track.FilePath;
					if (!string.IsNullOrEmpty(absPath))
					{
						string legacyId = MusicManager.GenerateID(absPath);
						if (track.Id == legacyId)
						{
							Log.Error("Legacy id detected: " + track.FilePath);
						}

						string relativePath = Path.GetRelativePath(m_config.MusicPath, absPath);
						if (!relativePath.StartsWith("..") && !Path.IsPathRooted(relativePath))
						{

							track.RelativeFilePath = relativePath;
							track.MarkDirty();

						}
						else
						{
							Log.Warning("Track FilePath not under MusicPath, skipping relative backfill: " + absPath);
						}
					}
				}

				//track ID migration - playlists will fix themselves up on re-import so don't worry about em
				string newId = MusicManager.GenerateTrackID(track.Artist, track.Album, track.DiscNumber, track.TrackNumber, track.Title);
				if (track.Id != newId)
				{
					tracksToRemove.Add(track.Id);
					m_musicData.Delete(eDataType.Track, track.Id);

					track.Id = newId;
					m_musicData.Save(eDataType.Track, track);
					tracksToAdd.Add(track);
				}
				

			}

			foreach (string deadId in tracksToRemove)
			{
				if (m_tracks.ContainsKey(deadId))
					m_tracks.Remove(deadId, out TrackData toss);
			}
			foreach (TrackData newTrack in tracksToAdd)
			{
				m_tracks[newTrack.Id] = newTrack;
			}
			Save();
		}
		/// <summary>
		/// Wire AlbumData.Tracks and ArtistData.Albums lists from the foreign-key
		/// columns now that all rows are loaded.
		/// </summary>
		private void WireUpReferences()
		{
			foreach (TrackData track in m_tracks.Values)
			{
				AlbumData album;
				if (m_albums.TryGetValue(track.AlbumId, out album))
				{
					album.Tracks.Add(track);
				}
				ArtistData artist;
				if (m_artists.TryGetValue(track.ArtistId, out artist))
				{
					track.ParentArtist = artist;
				}
			}

			foreach (AlbumData album in m_albums.Values)
			{
				ArtistData artist;
				if (m_artists.TryGetValue(album.ArtistId, out artist))
				{
					artist.Albums.Add(album);
				}
			}
		}

		/// <summary>
		/// Roll the per-track WeightedScore up into per-artist WeightedScore and
		/// per-user UserWeightedScore -- ArtistData's score fields are runtime
		/// derived state, not persisted, so they need to be recomputed at load.
		/// Without this the popular-artists sort and the popular carousel see all
		/// zeros.
		/// </summary>
		private void CalculateArtistScores()
		{
			foreach (ArtistData artist in m_artists.Values)
			{
				float totalScore = 0f;
				int scoredCount = 0;
				Dictionary<string, float> userTotals = new Dictionary<string, float>();
				Dictionary<string, int> userCounts = new Dictionary<string, int>();

				for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
				{
					AlbumData album = artist.Albums[albumIndex];
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						TrackData track = album.Tracks[trackIndex];

						if (track.Score.PlayCount > 0)
						{
							if (track.Score.WeightedScore > 1)
							{
								track.Score.WeightedScore = 1;
							}
							totalScore += track.Score.WeightedScore;
							scoredCount++;
						}

						foreach (string userName in track.UserScore.Keys)
						{
							TrackData.ScoreData userData = track.UserScore[userName];
							if (userData.PlayCount > 0)
							{
								if (!userTotals.ContainsKey(userName))
								{
									userTotals[userName] = 0f;
									userCounts[userName] = 0;
								}
								if (userData.WeightedScore > 1)
								{
									userData.WeightedScore = 1;
								}
								userTotals[userName] += userData.WeightedScore;
								userCounts[userName]++;
							}
						}
					}
				}

				if (scoredCount > 0)
				{
					artist.WeightedScore = totalScore / scoredCount;
				}
				foreach (string userName in userTotals.Keys)
				{
					artist.UserWeightedScore[userName] = userTotals[userName] / userCounts[userName];
				}
			}
		}


		public void RecordPlaybackEvent(string userName, PulseAnalytics analytics, DateTime occurredAt)
		{
			m_db.RecordPlaybackEvent(userName, analytics, occurredAt);
		}

		public Dictionary<string, ItemStats> GetItemStats(string userName, ePulseWireType mediaType)
		{
			return m_db.GetItemStats(userName, mediaType);
		}


		public List<User> GetAllUsers()
		{
			List<User> users = m_userData.GetAllUsers();
			return users;
		}

		public User LookupUserByName(string userName)
		{
			return m_userData.LookupUserByName(userName);
		}
		public User GetUser(string userId)
		{
			User userData = m_userData.GetUser(userId);
			return userData;
		}

		public bool IsTokenAuthorized(string userId, string token)
		{
			return m_userData.IsTokenAuthorized(userId, token);
		}

		public string CreateToken(string userId)
		{
			return m_userData.CreateToken(userId);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="name"></param>
		/// <param name="displayName"></param>
		/// <param name="isAdmin"></param>
		/// <returns>user id</returns>
		public User CreateUser(string name, string displayName, bool isAdmin, out string error)
		{
			return m_userData.CreateUser(name, displayName, isAdmin, out error);
		}

		public string UpdateUser(string userId, string newName, string displayName)
		{
			string error = m_userData.UpdateUser(userId, newName, displayName);
			if (!string.IsNullOrEmpty(error))
			{
				return error;
			}
			return "";
		}

		public void DeleteUser(string userId)
		{
			if (string.IsNullOrEmpty(userId)) { return; }

			m_userData.DeleteUser(userId);
			DeleteUserFromMusicData(userId);
			Save();
		}

		public string GetUserPasswordHash(string userId)
		{
			return m_userData.GetPasswordHash(userId);
		}

		public void SetUserPassword(string userId, string passwordHash)
		{
			m_userData.SetPassword(userId, passwordHash);
		}

		public bool AnyUserHasPassword()
		{
			return m_userData.AnyUserHasPassword();
		}

		public string CreateToken(string userId, string label)
		{
			return m_userData.CreateToken(userId, label);	
		}
	
		// Removes every in-memory trace of `userName` and flags the touched
		// entities dirty so the next Save rewrites their per-user rows.
		private void DeleteUserFromMusicData(string userId)
		{
			foreach (TrackData track in m_tracks.Values)
			{
				bool touched = false;
				if (track.UserScore.Remove(userId)) { touched = true; }
				if (track.Starred.Remove(userId)) { touched = true; }
				if (touched) { track.MarkDirty(); }
			}
			foreach (AlbumData album in m_albums.Values)
			{
				if (album.Starred.Remove(userId)) { album.MarkDirty(); }
			}
			foreach (ArtistData artist in m_artists.Values)
			{
				if (artist.Starred.Remove(userId)) { artist.MarkDirty(); }
			}
			foreach (PlaylistData playlist in m_playlists.Values)
			{
				if (playlist.UserLastPlayed.Remove(userId)) { playlist.MarkDirty(); }
			}
		}
	}
}
