using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pulse.Data
{
	public interface IPulseDatabase
	{
		int GetTrackCount();
		int GetAlbumCount();
		int GetArtistCount();
		PulseAnalyticsInfo GetAnalytics();

		TrackInfo GetTrack(string id);
		AlbumInfo GetAlbum(string id);
		ArtistInfo GetArtist(string id);
		List<TrackInfo> GetAllTracks();
		List<AlbumInfo> GetAllAlbums();
		List<ArtistInfo> GetAllArtists();
		bool TrackExists(string trackId);

		void SetRating(string trackId, int rating);
		void UpdateStar(string userName, string trackId, string albumId, string artistId, bool starred);
		ArtistInfo GetOrCreateArtist(string id, string name);
		AlbumInfo GetOrCreateAlbum(string id, string name, string artistId, string artistName, int year, string genre);
		void AddTrack(TrackInfo track, string albumId);
		bool RemoveTrack(string trackId);


		PlaylistInfo GetPlaylist(string id);
		List<PlaylistInfo> GetAllPlaylists(string userName);
		List<TrackInfo> GetPlaylistTracks(string playlistId);
		void DeletePlaylist(string playlistId);
		void CreateOrUpdate(PlaylistInfo playlist);
		void CreateOrUpdate(ArtistInfo artist);


		void Save(string reason);

		// Subsonic getPlayQueue / savePlayQueue / getBookmarks support (Flatline
		// #168). Written through directly to the persistence layer -- not cached
		// in memory and not part of the per-PulseInfo dirty flow. PulseSqliteDatabase
		// implements them against the v4 schema; PulseDatabaseBase keeps no-op
		// defaults.
		PlayQueueInfo GetPlayQueue(string userName);
		void SavePlayQueue(string userName, List<string> trackIds, string currentTrackId, long positionMs, string changedBy);
		List<BookmarkInfo> GetBookmarks(string userName);
		void SaveBookmark(string userName, string trackId, long positionMs, string comment);
		void DeleteBookmark(string userName, string trackId);

		// Append-only analytics event log (v6 schema). Each client-observed
		// playback state change lands as one immutable row, stored both
		// globally and per-user. Write-through, not cached. PulseSqliteDatabase
		// implements it; PulseDatabaseBase keeps a no-op default.
		void RecordAnalyticsEvent(string userName, PulseAnalytics analytics, DateTime occurredAt);

		// Settings-page CRUD over the v5 `users` table plus the per-user rows in
		// every other table (Flatline #201). PulseSqliteDatabase is the real
		// implementation; PulseDatabaseBase keeps in-memory no-op defaults.
		List<UserRecord> GetAllUsers();
		UserRecord GetUser(string name);
		string CreateUser(string name, string displayName, bool isAdmin);
		string UpdateUser(string oldName, string newName, string displayName, bool isAdmin);
		void DeleteUser(string userName);
	}

	public abstract class PulseDatabaseBase : IPulseDatabase
	{
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

		protected ConcurrentDictionary<string, TrackInfo> m_tracks = new ConcurrentDictionary<string, TrackInfo>();
		protected ConcurrentDictionary<string, AlbumInfo> m_albums = new ConcurrentDictionary<string, AlbumInfo>();
		protected ConcurrentDictionary<string, ArtistInfo> m_artists = new ConcurrentDictionary<string, ArtistInfo>();
		protected ConcurrentDictionary<string, PlaylistInfo> m_playlists = new ConcurrentDictionary<string, PlaylistInfo>();
		protected ConcurrentDictionary<string, PlaylistInfo> m_autoPlaylists = new ConcurrentDictionary<string, PlaylistInfo>();
		protected PulseAnalyticsInfo m_analytics = new PulseAnalyticsInfo();

		protected object m_saveLock = new object();
		
	
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
		public virtual void DeletePlaylist(string playlistId)
		{
			m_playlists.TryRemove(playlistId, out _);
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

		public virtual bool RemoveTrack(string trackId)
		{
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
			return true;
		}

		public abstract void Save(string reason);

		// Default no-op implementations (#168). PulseSqliteDatabase overrides them;
		// these remain as harmless base defaults.
		public virtual PlayQueueInfo GetPlayQueue(string userName)
		{
			return new PlayQueueInfo();
		}

		public virtual void SavePlayQueue(string userName, List<string> trackIds, string currentTrackId, long positionMs, string changedBy)
		{
		}

		public virtual List<BookmarkInfo> GetBookmarks(string userName)
		{
			return new List<BookmarkInfo>();
		}

		public virtual void SaveBookmark(string userName, string trackId, long positionMs, string comment)
		{
		}

		public virtual void DeleteBookmark(string userName, string trackId)
		{
		}

		public virtual void RecordAnalyticsEvent(string userName, PulseAnalytics analytics, DateTime occurredAt)
		{
		}

		// Base implementation walks the in-memory stores to synthesize a record
		// per observed name -- a base default only. PulseSqliteDatabase overrides
		// this to read the real users table and layers the in-memory counts on
		// top via PopulateUserCounts.
		public virtual List<UserRecord> GetAllUsers()
		{
			Dictionary<string, UserRecord> byName = new Dictionary<string, UserRecord>();
			PopulateUserCounts(byName, true);
			List<UserRecord> users = new List<UserRecord>(byName.Values);
			users.Sort(CompareUserRecordByName);
			return users;
		}

		public virtual UserRecord GetUser(string name)
		{
			List<UserRecord> all = GetAllUsers();
			for (int index = 0; index < all.Count; index++)
			{
				if (string.Equals(all[index].Name, name, StringComparison.Ordinal))
				{
					return all[index];
				}
			}
			return null;
		}

		public virtual string CreateUser(string name, string displayName, bool isAdmin)
		{
			return "";
		}

		public virtual string UpdateUser(string oldName, string newName, string displayName, bool isAdmin)
		{
			return "";
		}

		// Walks the in-memory stores and bumps ScoredTrackCount / StarredCount /
		// PlaylistLastPlayedCount on the matching record. When `createMissing` is
		// true (legacy file-backend path) a record is materialized for any name
		// that doesn't already have one in the dict; when false (SQLite path)
		// names that aren't in the `users` table are ignored -- they'd be orphan
		// per-user rows, not real users.
		protected void PopulateUserCounts(Dictionary<string, UserRecord> byName, bool createMissing)
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

		protected static int CompareUserRecordByName(UserRecord left, UserRecord right)
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

		// Cascades a rename across every in-memory store. Concrete subclasses
		// run this AFTER the SQL UPDATE succeeded so a constraint failure rolls
		// the SQL back without leaving in-memory in a half-renamed state.
		protected void RenameUserInMemory(string oldName, string newName)
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

		// Removes every in-memory trace of `userName` and flags the touched entities
		// dirty so the next Save rewrites their per-user rows. Concrete subclasses
		// override this to also wipe rows that aren't held in memory (bookmarks,
		// play queues) before chaining to the base implementation.
		public virtual void DeleteUser(string userName)
		{
			if (string.IsNullOrEmpty(userName)) { return; }

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
	}


}
