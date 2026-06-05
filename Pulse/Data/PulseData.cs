using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace Pulse.Data
{
	/// <summary>
	/// Domain / business layer: sole owner of the in-memory dictionaries
	/// (tracks, albums, artists, playlists, smart-playlists) and the
	/// runtime-derived state. Persistence is delegated to the stateless
	/// <see cref="PulseDatabase"/>, which translates load/save calls to and
	/// from SQLite. Callers (MusicManager and the routes layered above it)
	/// talk to PulseData -- never to PulseDatabase directly.
	/// </summary>
	public class PulseData
	{
		private ConcurrentDictionary<string, TrackInfo> m_tracks = new ConcurrentDictionary<string, TrackInfo>();
		private ConcurrentDictionary<string, AlbumInfo> m_albums = new ConcurrentDictionary<string, AlbumInfo>();
		private ConcurrentDictionary<string, ArtistInfo> m_artists = new ConcurrentDictionary<string, ArtistInfo>();
		private ConcurrentDictionary<string, PlaylistInfo> m_playlists = new ConcurrentDictionary<string, PlaylistInfo>();
		private ConcurrentDictionary<string, PlaylistInfo> m_autoPlaylists = new ConcurrentDictionary<string, PlaylistInfo>();
		private PulseAnalyticsInfo m_analytics = new PulseAnalyticsInfo();

		private PulseDatabase m_db = new PulseDatabase();

		public void Load()
		{
			Stopwatch sw = Stopwatch.StartNew();

			List<ArtistInfo> loadedArtists = m_db.LoadArtists();
			for (int index = 0; index < loadedArtists.Count; index++)
			{
				ArtistInfo artist = loadedArtists[index];
				m_artists[artist.Id] = artist;
			}

			List<AlbumInfo> loadedAlbums = m_db.LoadAlbums();
			for (int index = 0; index < loadedAlbums.Count; index++)
			{
				AlbumInfo album = loadedAlbums[index];
				m_albums[album.Id] = album;
			}

			List<TrackInfo> loadedTracks = m_db.LoadTracks();
			for (int index = 0; index < loadedTracks.Count; index++)
			{
				TrackInfo track = loadedTracks[index];
				m_tracks[track.Id] = track;
			}

			List<TrackUserScoreRow> loadedScores = m_db.LoadTrackUserScores();
			for (int index = 0; index < loadedScores.Count; index++)
			{
				TrackUserScoreRow row = loadedScores[index];
				TrackInfo track;
				if (m_tracks.TryGetValue(row.TrackId, out track))
				{
					track.UserScore[row.UserName] = row.Score;
				}
			}

			List<StarredRow> loadedStarred = m_db.LoadStarred();
			for (int index = 0; index < loadedStarred.Count; index++)
			{
				StarredRow row = loadedStarred[index];
				if (row.Kind == "track")
				{
					TrackInfo track;
					if (m_tracks.TryGetValue(row.EntityId, out track))
					{
						track.Starred[row.UserName] = row.Starred;
					}
				}
				else if (row.Kind == "album")
				{
					AlbumInfo album;
					if (m_albums.TryGetValue(row.EntityId, out album))
					{
						album.Starred[row.UserName] = row.Starred;
					}
				}
				else if (row.Kind == "artist")
				{
					ArtistInfo artist;
					if (m_artists.TryGetValue(row.EntityId, out artist))
					{
						artist.Starred[row.UserName] = row.Starred;
					}
				}
			}

			List<PlaylistInfo> loadedPlaylists = m_db.LoadPlaylists();
			for (int index = 0; index < loadedPlaylists.Count; index++)
			{
				PlaylistInfo playlist = loadedPlaylists[index];
				m_playlists[playlist.Id] = playlist;
			}

			m_analytics.RecentlyPlayed = m_db.LoadRecentlyPlayed();

			WireUpReferences();
			CalculateArtistScores();

			sw.Stop();
			Log.Info(-1, "PulseDatabase loaded in " + sw.ElapsedMilliseconds + "ms: "
				+ m_tracks.Count + " tracks, " + m_albums.Count + " albums, "
				+ m_artists.Count + " artists, " + m_playlists.Count + " playlists");
		}

		public void Save(string reason)
		{
			List<ArtistInfo> dirtyArtists = new List<ArtistInfo>();
			foreach (ArtistInfo artist in m_artists.Values)
			{
				if (artist.m_bIsDirty)
				{
					dirtyArtists.Add(artist);
				}
			}

			List<AlbumInfo> dirtyAlbums = new List<AlbumInfo>();
			foreach (AlbumInfo album in m_albums.Values)
			{
				if (album.m_bIsDirty)
				{
					dirtyAlbums.Add(album);
				}
			}

			List<TrackInfo> dirtyTracks = new List<TrackInfo>();
			foreach (TrackInfo track in m_tracks.Values)
			{
				if (track.m_bIsDirty)
				{
					dirtyTracks.Add(track);
				}
			}

			List<PlaylistInfo> dirtyPlaylists = new List<PlaylistInfo>();
			foreach (PlaylistInfo playlist in m_playlists.Values)
			{
				if (playlist.m_bIsDirty)
				{
					dirtyPlaylists.Add(playlist);
				}
			}

			m_db.Save(reason, dirtyArtists, dirtyAlbums, dirtyTracks, dirtyPlaylists, m_analytics);
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
		public PulseAnalyticsInfo GetAnalytics()
		{
			return m_analytics;
		}

		public TrackInfo GetTrack(string id)
		{
			TrackInfo track;
			m_tracks.TryGetValue(id, out track);
			return track;
		}

		public AlbumInfo GetAlbum(string id)
		{
			AlbumInfo album;
			m_albums.TryGetValue(id, out album);
			return album;
		}

		public ArtistInfo GetArtist(string id)
		{
			ArtistInfo artist;
			m_artists.TryGetValue(id, out artist);
			return artist;
		}

		public List<TrackInfo> GetAllTracks()
		{
			return new List<TrackInfo>(m_tracks.Values);
		}

		public List<AlbumInfo> GetAllAlbums()
		{
			return new List<AlbumInfo>(m_albums.Values);
		}

		private static int CompareArtistByName(ArtistInfo left, ArtistInfo right)
		{
			return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
		}

		public List<ArtistInfo> GetAllArtists()
		{
			List<ArtistInfo> list = new List<ArtistInfo>(m_artists.Values);
			list.Sort(CompareArtistByName);
			return list;
		}

		public bool TrackExists(string trackId)
		{
			return m_tracks.ContainsKey(trackId);
		}

		public void SetRating(string trackId, int rating)
		{
			TrackInfo track;
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
				TrackInfo track;
				if (m_tracks.TryGetValue(trackId, out track))
				{
					track.Starred[userName] = starred;
					track.m_bIsDirty = true;
				}
			}

			if (!string.IsNullOrEmpty(albumId))
			{
				AlbumInfo album;
				if (m_albums.TryGetValue(albumId, out album))
				{
					album.Starred[userName] = starred;
					album.m_bIsDirty = true;
				}
			}

			if (!string.IsNullOrEmpty(artistId))
			{
				ArtistInfo artist;
				if (m_artists.TryGetValue(artistId, out artist))
				{
					artist.Starred[userName] = starred;
					artist.m_bIsDirty = true;
				}
			}
		}

		public PlaylistInfo GetPlaylist(string id)
		{
			PlaylistInfo playlist;
			if (m_playlists.TryGetValue(id, out playlist))
			{
				return playlist;
			}
			m_autoPlaylists.TryGetValue(id, out playlist);
			return playlist;
		}

		public List<PlaylistInfo> GetAllPlaylists(string userName)
		{
			RebuildSmartPlaylists(userName);
			List<PlaylistInfo> list = new List<PlaylistInfo>(m_playlists.Values);
			list.AddRange(m_autoPlaylists.Values);
			return list;
		}

		public List<TrackInfo> GetPlaylistTracks(string playlistId)
		{
			PlaylistInfo playlist;
			if (!m_playlists.TryGetValue(playlistId, out playlist))
			{
				if (!m_autoPlaylists.TryGetValue(playlistId, out playlist))
				{
					return new List<TrackInfo>();
				}
			}

			List<TrackInfo> tracks = new List<TrackInfo>();
			for (int index = 0; index < playlist.TrackIds.Count; index++)
			{
				TrackInfo track;
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
		}

		public void CreateOrUpdate(PlaylistInfo playlist)
		{
			m_playlists[playlist.Id] = playlist;
			m_playlists[playlist.Id].m_bIsDirty = true;
		}

		public void CreateOrUpdate(ArtistInfo artistInfo)
		{
			ArtistInfo artist;
			if (!m_artists.TryGetValue(artistInfo.Id, out artist))
			{
				artist = new ArtistInfo();
				m_artists[artistInfo.Id] = artist;
			}
			artist.Id = artistInfo.Id;
			artist.Name = artistInfo.Name;
			artist.m_bIsDirty = true;
		}

		public ArtistInfo GetOrCreateArtist(string id, string name)
		{
			ArtistInfo artist;
			if (m_artists.TryGetValue(id, out artist))
			{
				return artist;
			}

			artist = new ArtistInfo();
			artist.Id = id;
			artist.Name = name;
			artist.m_bIsDirty = true;
			m_artists[id] = artist;
			return artist;
		}

		public AlbumInfo GetOrCreateAlbum(string id, string name, string artistId, string artistName, int year, string genre)
		{
			AlbumInfo album;
			if (m_albums.TryGetValue(id, out album))
			{
				return album;
			}

			ArtistInfo artist = GetArtist(artistId);

			album = new AlbumInfo();
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

		public void AddTrack(TrackInfo track, string albumId)
		{
			m_tracks[track.Id] = track;
			track.m_bIsDirty = true;

			AlbumInfo album;
			if (m_albums.TryGetValue(albumId, out album))
			{
				album.Tracks.Add(track);
				album.m_bIsDirty = true;
			}
		}

		public bool RemoveTrack(string trackId)
		{
			// Capture the parent ids before mutating the in-memory maps. We
			// remove the album from m_albums when its last track goes, and the
			// artist from m_artists when its last album goes -- mirror that
			// into SQL so orphaned album/artist rows (and their starred rows)
			// don't survive and reappear on the next Load (#302).
			string albumId = null;
			string artistId = null;
			TrackInfo existing;
			if (m_tracks.TryGetValue(trackId, out existing))
			{
				albumId = existing.AlbumId;
				artistId = existing.ArtistId;
			}

			TrackInfo track;
			if (!m_tracks.TryRemove(trackId, out track))
			{
				return false;
			}

			AlbumInfo album;
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

					ArtistInfo artist;
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

			bool albumEmptied = !string.IsNullOrEmpty(albumId) && !m_albums.ContainsKey(albumId);
			bool artistEmptied = !string.IsNullOrEmpty(artistId) && !m_artists.ContainsKey(artistId);

			m_db.DeleteTrackAndOrphans(trackId, albumId, artistId, albumEmptied, artistEmptied);
			return true;
		}

		public PlayQueueInfo GetPlayQueue(string userName)
		{
			return m_db.GetPlayQueue(userName);
		}

		public void SavePlayQueue(string userName, List<string> trackIds, string currentTrackId, long positionMs, string changedBy)
		{
			m_db.SavePlayQueue(userName, trackIds, currentTrackId, positionMs, changedBy);
		}

		public List<BookmarkInfo> GetBookmarks(string userName)
		{
			return m_db.GetBookmarks(userName);
		}

		public void SaveBookmark(string userName, string trackId, long positionMs, string comment)
		{
			m_db.SaveBookmark(userName, trackId, positionMs, comment);
		}

		public void DeleteBookmark(string userName, string trackId)
		{
			m_db.DeleteBookmark(userName, trackId);
		}

		public void RecordAnalyticsEvent(string userName, PulseAnalytics analytics, DateTime occurredAt)
		{
			m_db.RecordAnalyticsEvent(userName, analytics, occurredAt);
		}

		public Dictionary<string, AnalyticsAggregate> GetStartedAggregates(string userName, eDataType mediaType)
		{
			return m_db.GetStartedAggregates(userName, mediaType);
		}

		// Reads the users table via the persistence layer, layers the
		// in-memory per-user counts on top, then sorts. Names that have
		// per-user rows but no users-table entry (e.g. scrobbled after the
		// user was deleted) are surfaced as orphan records with
		// Created=MinValue so the operator can spot and clean them up.
		public List<UserRecord> GetAllUsers()
		{
			Dictionary<string, UserRecord> byName = m_db.ReadAllUsers();
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
			string result = m_db.UpdateUserRow(oldName, newName, displayName, isAdmin);
			if (string.Equals(result, "", StringComparison.Ordinal) && !string.Equals(oldName, newName, StringComparison.Ordinal))
			{
				RenameUserInMemory(oldName, newName);
			}
			return result;
		}

		// Two-stage wipe: SQL transaction first (non-cached per-user rows
		// plus the users-table row), then the in-memory cascade to scrub the
		// dicts and flag affected tracks/albums/artists/playlists dirty so
		// the upcoming Save rewrites their starred / score / last-played
		// rows without this user.
		public void DeleteUser(string userName)
		{
			if (string.IsNullOrEmpty(userName)) { return; }

			m_db.DeleteUserRows(userName);

			foreach (TrackInfo track in m_tracks.Values)
			{
				bool touched = false;
				if (track.UserScore.Remove(userName)) { touched = true; }
				if (track.Starred.Remove(userName)) { touched = true; }
				if (touched) { track.m_bIsDirty = true; }
			}
			foreach (AlbumInfo album in m_albums.Values)
			{
				if (album.Starred.Remove(userName)) { album.m_bIsDirty = true; }
			}
			foreach (ArtistInfo artist in m_artists.Values)
			{
				if (artist.Starred.Remove(userName)) { artist.m_bIsDirty = true; }
			}
			foreach (PlaylistInfo playlist in m_playlists.Values)
			{
				if (playlist.UserLastPlayed.Remove(userName)) { playlist.m_bIsDirty = true; }
			}

			Save("delete-user");
		}

		// Walks the in-memory stores and bumps ScoredTrackCount / StarredCount /
		// PlaylistLastPlayedCount on the matching record. When `createMissing` is
		// true (legacy file-backend path) a record is materialized for any name
		// that doesn't already have one in the dict; when false (SQLite path)
		// names that aren't in the `users` table are ignored -- they'd be orphan
		// per-user rows, not real users.
		private void PopulateUserCounts(Dictionary<string, UserRecord> byName, bool createMissing)
		{
			foreach (TrackInfo track in m_tracks.Values)
			{
				foreach (KeyValuePair<string, ScoreData> entry in track.UserScore)
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
			foreach (AlbumInfo album in m_albums.Values)
			{
				foreach (KeyValuePair<string, bool> entry in album.Starred)
				{
					if (!entry.Value) { continue; }
					UserRecord record = GetOrLookupUserRecord(byName, entry.Key, createMissing);
					if (record != null) { record.StarredCount++; }
				}
			}
			foreach (ArtistInfo artist in m_artists.Values)
			{
				foreach (KeyValuePair<string, bool> entry in artist.Starred)
				{
					if (!entry.Value) { continue; }
					UserRecord record = GetOrLookupUserRecord(byName, entry.Key, createMissing);
					if (record != null) { record.StarredCount++; }
				}
			}
			foreach (PlaylistInfo playlist in m_playlists.Values)
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

		// Cascades a rename across every in-memory store. Called AFTER the SQL
		// UPDATE succeeded so a constraint failure rolls the SQL back without
		// leaving in-memory in a half-renamed state.
		private void RenameUserInMemory(string oldName, string newName)
		{
			if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) { return; }
			if (string.Equals(oldName, newName, StringComparison.Ordinal)) { return; }

			foreach (TrackInfo track in m_tracks.Values)
			{
				ScoreData score;
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
			foreach (AlbumInfo album in m_albums.Values)
			{
				bool starred;
				if (album.Starred.TryGetValue(oldName, out starred))
				{
					album.Starred.Remove(oldName);
					album.Starred[newName] = starred;
				}
			}
			foreach (ArtistInfo artist in m_artists.Values)
			{
				bool starred;
				if (artist.Starred.TryGetValue(oldName, out starred))
				{
					artist.Starred.Remove(oldName);
					artist.Starred[newName] = starred;
				}
			}
			foreach (PlaylistInfo playlist in m_playlists.Values)
			{
				DateTime lastPlayed;
				if (playlist.UserLastPlayed.TryGetValue(oldName, out lastPlayed))
				{
					playlist.UserLastPlayed.Remove(oldName);
					playlist.UserLastPlayed[newName] = lastPlayed;
				}
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

			List<TrackInfo> scoredTracks = new List<TrackInfo>();
			List<ArtistInfo> scoredArtists = new List<ArtistInfo>();
			List<TrackInfo> unplayedTracks = new List<TrackInfo>();

			foreach (ArtistInfo artistInfo in m_artists.Values)
			{
				if (artistInfo.WeightedScore > 0)
				{
					scoredArtists.Add(artistInfo);
				}
			}

			SmartPlaylist.CategorizeTracks(m_tracks.Values, userName, scoredTracks, unplayedTracks);

			Random rng = new Random();
			PlaylistInfo playlist = SmartPlaylist.BuildSmartPlaylist(playlistId, "Top Rated (" + userName + ")", scoredTracks, scoredArtists, unplayedTracks, userName, rng);
			m_autoPlaylists[playlistId] = playlist;
		}

		/// <summary>
		/// Wire AlbumInfo.Tracks and ArtistInfo.Albums lists from the foreign-key
		/// columns now that all rows are loaded.
		/// </summary>
		private void WireUpReferences()
		{
			foreach (TrackInfo track in m_tracks.Values)
			{
				AlbumInfo album;
				if (m_albums.TryGetValue(track.AlbumId, out album))
				{
					album.Tracks.Add(track);
				}
				ArtistInfo artist;
				if (m_artists.TryGetValue(track.ArtistId, out artist))
				{
					track.ParentArtist = artist;
				}
			}

			foreach (AlbumInfo album in m_albums.Values)
			{
				ArtistInfo artist;
				if (m_artists.TryGetValue(album.ArtistId, out artist))
				{
					artist.Albums.Add(album);
				}
			}
		}

		/// <summary>
		/// Roll the per-track WeightedScore up into per-artist WeightedScore and
		/// per-user UserWeightedScore -- ArtistInfo's score fields are runtime
		/// derived state, not persisted, so they need to be recomputed at load.
		/// Without this the popular-artists sort and the popular carousel see all
		/// zeros.
		/// </summary>
		private void CalculateArtistScores()
		{
			foreach (ArtistInfo artist in m_artists.Values)
			{
				float totalScore = 0f;
				int scoredCount = 0;
				Dictionary<string, float> userTotals = new Dictionary<string, float>();
				Dictionary<string, int> userCounts = new Dictionary<string, int>();

				for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
				{
					AlbumInfo album = artist.Albums[albumIndex];
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						TrackInfo track = album.Tracks[trackIndex];

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
							ScoreData userData = track.UserScore[userName];
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
	}
}
