using Microsoft.AspNetCore.Http.HttpResults;
using Pulse.Database;
using Pulse.DataStorage;
using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Pulse.Data
{
	public class PulseData
	{
		private ConcurrentDictionary<string, TrackData> m_tracks = new ConcurrentDictionary<string, TrackData>();
		private ConcurrentDictionary<string, AlbumData> m_albums = new ConcurrentDictionary<string, AlbumData>();
		private ConcurrentDictionary<string, ArtistData> m_artists = new ConcurrentDictionary<string, ArtistData>();
		private ConcurrentDictionary<string, PlaylistData> m_playlists = new ConcurrentDictionary<string, PlaylistData>();
		private ConcurrentDictionary<string, PlaylistData> m_autoPlaylists = new ConcurrentDictionary<string, PlaylistData>();
		private PulseAnalyticsData m_analytics = new PulseAnalyticsData();

		private PulseDB m_db = new PulseDB();
		private PulseDataStore m_musicData;
		private PulseDataStore m_userData;

		private PulseConfig m_config;
		public PulseData(PulseConfig config)
		{
			m_config = config;

			string musicDB = "music.db";
			string userDB = "user.db";
#if DEBUG
			musicDB = "music_staging.db";
			userDB = "user_staging.db";
#endif
			string dbPath = Path.Combine(m_config.PulseDataPath, musicDB);
			m_musicData = new PulseDataStore(dbPath);

			dbPath = Path.Combine(m_config.PulseDataPath, userDB);
			m_userData = new PulseDataStore(dbPath);

			
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

		public void SetRating(string trackId, int rating)
		{
			TrackData track;
			if (m_tracks.TryGetValue(trackId, out track))
			{
				track.Rating = rating;
				track.m_bIsDirty = true;
			}
		}

		public void UpdateStar(string userName, string trackId, string albumId, string artistId, bool starred)
		{
			if (!string.IsNullOrEmpty(trackId))
			{
				TrackData track;
				if (m_tracks.TryGetValue(trackId, out track))
				{
					track.Starred[userName] = starred;
					track.m_bIsDirty = true;
				}
			}

			if (!string.IsNullOrEmpty(albumId))
			{
				AlbumData album;
				if (m_albums.TryGetValue(albumId, out album))
				{
					album.Starred[userName] = starred;
					album.m_bIsDirty = true;
				}
			}

			if (!string.IsNullOrEmpty(artistId))
			{
				ArtistData artist;
				if (m_artists.TryGetValue(artistId, out artist))
				{
					artist.Starred[userName] = starred;
					artist.m_bIsDirty = true;
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
			m_autoPlaylists.TryGetValue(id, out playlist);
			return playlist;
		}

		public List<PlaylistData> GetAllPlaylists(string userName)
		{
			RebuildSmartPlaylists(userName);
			List<PlaylistData> list = new List<PlaylistData>(m_playlists.Values);
			list.AddRange(m_autoPlaylists.Values);
			return list;
		}

		public List<TrackData> GetPlaylistTracks(string playlistId)
		{
			PlaylistData playlist;
			if (!m_playlists.TryGetValue(playlistId, out playlist))
			{
				if (!m_autoPlaylists.TryGetValue(playlistId, out playlist))
				{
					return new List<TrackData>();
				}
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
			m_db.DeletePlaylistRows(playlistId);
			m_musicData.Delete(eDataType.Playlist, playlistId);
		}

		public void CreateOrUpdate(PlaylistData playlist)
		{
			m_playlists[playlist.Id] = playlist;
			m_playlists[playlist.Id].m_bIsDirty = true;
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
			artist.m_bIsDirty = true;
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
			artist.m_bIsDirty = true;
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
				artist.m_bIsDirty = true;
			}

			return album;
		}

		public void AddTrack(TrackData track, string albumId)
		{
			m_tracks[track.Id] = track;
			track.m_bIsDirty = true;

			AlbumData album;
			if (m_albums.TryGetValue(albumId, out album))
			{
				album.Tracks.Add(track);
				album.m_bIsDirty = true;
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

			m_db.DeleteTrackRows(trackId, albumId, artistId, albumEmptied, artistEmptied);
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
				Log.Warning(-1, "Debugger attached: forcing Staging environment (config said '" + environmentName + "'). Debug sessions never touch production data.");
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
			Pulse.Database.PulseDBConnector.SetDatabaseFilePath(sqlitePath);
			Pulse.Database.PulseDBMigrations.RunMigrations();
			Log.Info(-1, "Pulse DB: env=" + environmentName + " path=" + sqlitePath);


			Stopwatch sw = Stopwatch.StartNew();

			List<ArtistData> artists = m_db.LoadArtists();
			for (int index = 0; index < artists.Count; index++)
			{
				m_artists[artists[index].Id] = artists[index];
			}

			List<AlbumData> albums = m_db.LoadAlbums();
			for (int index = 0; index < albums.Count; index++)
			{
				m_albums[albums[index].Id] = albums[index];
			}

			List<TrackData> tracks = m_db.LoadTracks();
			for (int index = 0; index < tracks.Count; index++)
			{
				m_tracks[tracks[index].Id] = tracks[index];
			}

			List<TrackUserScoreRow> userScoreRows = m_db.LoadTrackUserScores();
			for (int index = 0; index < userScoreRows.Count; index++)
			{
				TrackUserScoreRow row = userScoreRows[index];
				TrackData track;
				if (!m_tracks.TryGetValue(row.TrackId, out track))
				{
					continue;
				}
				track.UserScore[row.UserName] = row.Score;
			}

			List<StarredRow> starredRows = m_db.LoadStarred();
			for (int index = 0; index < starredRows.Count; index++)
			{
				StarredRow row = starredRows[index];
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

			// Build a legacy-to-current ID lookup so playlists that reference
			// the old (pre-move) track IDs can be remapped on load.
			Dictionary<string, string> legacyToCurrentId = new Dictionary<string, string>();
			foreach (TrackData track in m_tracks.Values)
			{
				if (!string.IsNullOrEmpty(track.LegacyId) && track.LegacyId != track.Id)
				{
					legacyToCurrentId[track.LegacyId] = track.Id;
				}
			}

			List<PlaylistData> playlists = m_db.LoadPlaylists();
			for (int index = 0; index < playlists.Count; index++)
			{
				PlaylistData playlist = playlists[index];

				if (legacyToCurrentId.Count > 0)
				{
					for (int trackIndex = 0; trackIndex < playlist.TrackIds.Count; trackIndex++)
					{
						string currentId;
						if (legacyToCurrentId.TryGetValue(playlist.TrackIds[trackIndex], out currentId))
						{
							playlist.TrackIds[trackIndex] = currentId;
							playlist.m_bIsDirty = true;
						}
					}
				}
				m_playlists[playlist.Id] = playlist;
			}

			List<string> recentlyPlayed = m_db.LoadRecentlyPlayed();
			for (int index = 0; index < recentlyPlayed.Count; index++)
			{
				m_analytics.RecentlyPlayed.Add(recentlyPlayed[index]);
			}

			WireUpReferences();
			CalculateArtistScores();

			sw.Stop();
			Log.Info(-1, "PulseData loaded in " + sw.ElapsedMilliseconds + "ms: "
				+ m_tracks.Count + " tracks, " + m_albums.Count + " albums, "
				+ m_artists.Count + " artists, " + m_playlists.Count + " playlists");

			SaveNewDB();
		}

		public void LoadNewDB()
		{
			List<TrackData> tracks = m_musicData.LoadList<TrackData>(eDataType.Track);
			m_tracks.Clear();
			foreach (TrackData track in tracks)
			{
				m_tracks[track.Id] = track;
			}
			List<AlbumData> albums = m_musicData.LoadList<AlbumData>(eDataType.Album);
			m_albums.Clear();
			foreach (AlbumData album in albums)
			{
				m_albums[album.Id] = album;
			}
			List<ArtistData> artists = m_musicData.LoadList<ArtistData>(eDataType.Artist);
			m_artists.Clear();
			foreach (ArtistData artist in artists)
			{
				m_artists[artist.Id] = artist;
			}
			List<PlaylistData> playlists = m_musicData.LoadList<PlaylistData>(eDataType.Playlist);
			m_playlists.Clear();
			foreach (PlaylistData playlist in playlists)
			{
				m_playlists[playlist.Id] = playlist;
			}
		}

		public void SaveNewDB()
		{
			m_musicData.SaveList(eDataType.Track, new List<TrackData>(m_tracks.Values));
			m_musicData.SaveList(eDataType.Album, new List<AlbumData> (m_albums.Values));
			m_musicData.SaveList(eDataType.Artist, new List<ArtistData> (m_artists.Values));
			m_musicData.SaveList(eDataType.Playlist, new List<PlaylistData>(m_playlists.Values));	
			m_musicData.Save(eDataType.PulseAnalytics, m_analytics);
		}

		public void Save(string reason)
		{
			List<ArtistData> dirtyArtists = new List<ArtistData>();
			foreach (ArtistData artist in m_artists.Values)
			{
				if (artist.m_bIsDirty) { dirtyArtists.Add(artist); }
			}

			List<AlbumData> dirtyAlbums = new List<AlbumData>();
			foreach (AlbumData album in m_albums.Values)
			{
				if (album.m_bIsDirty) { dirtyAlbums.Add(album); }
			}

			List<TrackData> dirtyTracks = new List<TrackData>();
			foreach (TrackData track in m_tracks.Values)
			{
				if (track.m_bIsDirty) { dirtyTracks.Add(track); }
			}

			List<PlaylistData> dirtyPlaylists = new List<PlaylistData>();
			foreach (PlaylistData playlist in m_playlists.Values)
			{
				if (playlist.m_bIsDirty) { dirtyPlaylists.Add(playlist); }
			}

			m_db.Save(reason, dirtyArtists, dirtyAlbums, dirtyTracks, dirtyPlaylists, m_analytics);

			m_musicData.SaveList<ArtistData>(eDataType.Artist, dirtyArtists);
			m_musicData.SaveList<AlbumData>(eDataType.Album, dirtyAlbums);
			m_musicData.SaveList<TrackData>(eDataType.Track, dirtyTracks);
			m_musicData.SaveList<PlaylistData>(eDataType.Playlist, dirtyPlaylists);
			m_musicData.Save<PulseAnalyticsData>(eDataType.PulseAnalytics, m_analytics);

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

		// SELECT from the users table, then layer the in-memory per-user counts
		// on top. Names that have per-user rows but no users-table entry (e.g.
		// scrobbled after the user was deleted) are surfaced as orphan records
		// with Created=MinValue so the operator can spot and clean them up.
		public List<UserRecord> GetAllUsers()
		{
			Dictionary<string, UserRecord> byName = new Dictionary<string, UserRecord>();
			List<UserRecord> rows = m_db.ReadAllUsers();
			for (int index = 0; index < rows.Count; index++)
			{
				byName[rows[index].Name] = rows[index];
			}
			PopulateUserCounts(byName, true);
			List<UserRecord> users = new List<UserRecord>(byName.Values);
			users.Sort(CompareUserRecordByName);
			return users;
		}

		public UserRecord GetUser(string name)
		{
			return m_db.ReadUser(name);
		}

		public string CreateUser(string name, string displayName, bool isAdmin)
		{
			return m_db.InsertUser(name, displayName, isAdmin);
		}

		public string UpdateUser(string oldName, string newName, string displayName, bool isAdmin)
		{
			string error = m_db.UpdateUserRow(oldName, newName, displayName, isAdmin);
			if (!string.IsNullOrEmpty(error))
			{
				return error;
			}
			if (!string.Equals(oldName, newName, StringComparison.Ordinal))
			{
				RenameUserInMemory(oldName, newName);
			}
			return "";
		}

		// Two-stage wipe: rows first (non-cached per-user tables + users row),
		// then scrub the in-memory dicts and flag affected entities dirty so
		// the upcoming Save rewrites their starred / score / last-played rows
		// without this user.
		public void DeleteUser(string userName)
		{
			if (string.IsNullOrEmpty(userName)) { return; }

			m_db.DeleteUserRows(userName);
			DeleteUserInMemory(userName);
			Save("delete-user");
		}

		public string GetUserPasswordHash(string name)
		{
			return m_db.ReadUserPasswordHash(name);
		}

		public void SetUserPassword(string name, string passwordHash)
		{
			m_db.SetUserPassword(name, passwordHash);
		}

		public bool AnyUserHasPassword()
		{
			return m_db.AnyUserHasPassword();
		}

		public void InsertToken(string token, string userName, string label)
		{
			m_db.InsertToken(token, userName, label);
		}

		public List<TokenRow> GetAllTokens()
		{
			return m_db.GetAllTokens();
		}

		public List<TokenRow> GetTokensForUser(string userName)
		{
			return m_db.GetTokensForUser(userName);
		}

		public string LookupTokenUser(string token)
		{
			return m_db.LookupTokenUser(token);
		}

		/// <summary>
		/// FUCK THIS FUNCTION WHO GIVES A SHIT
		/// </summary>
		/// <param name="token"></param>
		public void UpdateTokenLastUsed(string token)
		{
			m_db.UpdateTokenLastUsed(token);
		}

		public void DeleteToken(string token)
		{
			string id = "";//WHERE THE FUCK IS MY OBJECT ID
			m_userData.Delete(eDataType.User, id);
			m_db.DeleteToken(token);
		}

		// Walks the in-memory stores and bumps ScoredTrackCount / StarredCount /
		// PlaylistLastPlayedCount on the matching record. When `createMissing` is
		// true a record is materialized for any name that doesn't already have
		// one in the dict -- this surfaces orphan per-user rows whose users-row
		// no longer exists.
		private void PopulateUserCounts(Dictionary<string, UserRecord> byName, bool createMissing)
		{
			foreach (TrackData track in m_tracks.Values)
			{
				foreach (KeyValuePair<string, TrackData.ScoreData> entry in track.UserScore)
				{
					UserRecord record = GetOrLookupUserRecord(byName, entry.Key, createMissing);
					if (record != null) { record.ScoredTrackCount++; }
				}
				foreach (KeyValuePair<string, bool> entry in track.Starred)
				{
					if (!entry.Value) { continue; }
					UserRecord record = GetOrLookupUserRecord(byName, entry.Key, createMissing);
					if (record != null) { record.StarredCount++; }
				}
			}
			foreach (AlbumData album in m_albums.Values)
			{
				foreach (KeyValuePair<string, bool> entry in album.Starred)
				{
					if (!entry.Value) { continue; }
					UserRecord record = GetOrLookupUserRecord(byName, entry.Key, createMissing);
					if (record != null) { record.StarredCount++; }
				}
			}
			foreach (ArtistData artist in m_artists.Values)
			{
				foreach (KeyValuePair<string, bool> entry in artist.Starred)
				{
					if (!entry.Value) { continue; }
					UserRecord record = GetOrLookupUserRecord(byName, entry.Key, createMissing);
					if (record != null) { record.StarredCount++; }
				}
			}
			foreach (PlaylistData playlist in m_playlists.Values)
			{
				foreach (KeyValuePair<string, DateTime> entry in playlist.UserLastPlayed)
				{
					UserRecord record = GetOrLookupUserRecord(byName, entry.Key, createMissing);
					if (record != null) { record.PlaylistLastPlayedCount++; }
				}
			}
		}

		private static int CompareUserRecordByName(UserRecord left, UserRecord right)
		{
			return string.Compare(left.Name, right.Name, StringComparison.Ordinal);
		}

		private static UserRecord GetOrLookupUserRecord(Dictionary<string, UserRecord> byName, string userName, bool createMissing)
		{
			UserRecord record;
			if (byName.TryGetValue(userName, out record))
			{
				return record;
			}
			if (!createMissing)
			{
				return null;
			}
			record = new UserRecord();
			record.Name = userName;
			record.DisplayName = userName;
			byName[userName] = record;
			return record;
		}

		// Cascades a rename across every in-memory store. Runs AFTER the SQL
		// UPDATE succeeded so a constraint failure rolls the SQL back without
		// leaving in-memory in a half-renamed state.
		private void RenameUserInMemory(string oldName, string newName)
		{
			if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) { return; }
			if (string.Equals(oldName, newName, StringComparison.Ordinal)) { return; }

			foreach (TrackData track in m_tracks.Values)
			{
				TrackData.ScoreData score;
				if (track.UserScore.TryGetValue(oldName, out score))
				{
					track.UserScore.Remove(oldName);
					track.UserScore[newName] = score;
				}
				bool starred;
				if (track.Starred.TryGetValue(oldName, out starred))
				{
					track.Starred.Remove(oldName);
					track.Starred[newName] = starred;
				}
			}
			foreach (AlbumData album in m_albums.Values)
			{
				bool starred;
				if (album.Starred.TryGetValue(oldName, out starred))
				{
					album.Starred.Remove(oldName);
					album.Starred[newName] = starred;
				}
			}
			foreach (ArtistData artist in m_artists.Values)
			{
				bool starred;
				if (artist.Starred.TryGetValue(oldName, out starred))
				{
					artist.Starred.Remove(oldName);
					artist.Starred[newName] = starred;
				}
			}
			foreach (PlaylistData playlist in m_playlists.Values)
			{
				DateTime lastPlayed;
				if (playlist.UserLastPlayed.TryGetValue(oldName, out lastPlayed))
				{
					playlist.UserLastPlayed.Remove(oldName);
					playlist.UserLastPlayed[newName] = lastPlayed;
				}
			}
		}

		// Removes every in-memory trace of `userName` and flags the touched
		// entities dirty so the next Save rewrites their per-user rows.
		private void DeleteUserInMemory(string userName)
		{
			foreach (TrackData track in m_tracks.Values)
			{
				bool touched = false;
				if (track.UserScore.Remove(userName)) { touched = true; }
				if (track.Starred.Remove(userName)) { touched = true; }
				if (touched) { track.m_bIsDirty = true; }
			}
			foreach (AlbumData album in m_albums.Values)
			{
				if (album.Starred.Remove(userName)) { album.m_bIsDirty = true; }
			}
			foreach (ArtistData artist in m_artists.Values)
			{
				if (artist.Starred.Remove(userName)) { artist.m_bIsDirty = true; }
			}
			foreach (PlaylistData playlist in m_playlists.Values)
			{
				if (playlist.UserLastPlayed.Remove(userName)) { playlist.m_bIsDirty = true; }
			}
		}

		private void RebuildSmartPlaylists(string userName)
		{
			RebuildSmartPlaylist("Shared", null);
			if (!string.IsNullOrEmpty(userName))
			{
				RebuildSmartPlaylist(userName, userName);
			}
		}

		private void RebuildSmartPlaylist(string playlistName, string userName)
		{
			string playlistId = MusicManager.GenerateID("smart/" + playlistName);

			List<TrackData> scoredTracks = new List<TrackData>();
			List<ArtistData> scoredArtists = new List<ArtistData>();
			List<TrackData> unplayedTracks = new List<TrackData>();

			foreach (ArtistData ArtistData in m_artists.Values)
			{
				if (ArtistData.WeightedScore > 0)
				{
					scoredArtists.Add(ArtistData);
				}
			}

			SmartPlaylist.CategorizeTracks(m_tracks.Values, userName, scoredTracks, unplayedTracks);

			Random rng = new Random();
			PlaylistData playlist = SmartPlaylist.BuildSmartPlaylist(playlistId, "Top Rated (" + userName + ")", scoredTracks, scoredArtists, unplayedTracks, userName, rng);
			m_autoPlaylists[playlistId] = playlist;
		}
	}
}
