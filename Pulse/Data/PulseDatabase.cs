using Microsoft.Data.Sqlite;
using Pulse.Database;
using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace Pulse.Data
{
	/// <summary>
	/// Per-id rollup of the analytics event log for one media type: how many
	/// 'Started' events the item has accrued and when it was most recently
	/// started. Produced by <see cref="PulseDatabase.GetStartedAggregates"/>.
	/// </summary>
	public class AnalyticsAggregate
	{
		public int PlayCount { get; set; }
		public DateTime LastPlayed { get; set; }
	}

	/// <summary>
	/// SQLite persistence layer. Holds references to the in-memory dictionaries
	/// owned by <see cref="PulseData"/> and translates them to/from SQLite. No
	/// derived state, no graph wiring, no score math -- that all lives in
	/// PulseData.
	///
	/// Migration path: see Database/Migrations.cs. Add a new MigrationStep to
	/// evolve the schema; never edit a shipped one.
	/// </summary>
	public class PulseDatabase
	{
		private ConcurrentDictionary<string, TrackInfo> m_tracks;
		private ConcurrentDictionary<string, AlbumInfo> m_albums;
		private ConcurrentDictionary<string, ArtistInfo> m_artists;
		private ConcurrentDictionary<string, PlaylistInfo> m_playlists;
		private PulseAnalyticsInfo m_analytics;

		private object m_saveLock = new object();

		public PulseDatabase(ConcurrentDictionary<string, TrackInfo> tracks, ConcurrentDictionary<string, AlbumInfo> albums, ConcurrentDictionary<string, ArtistInfo> artists, ConcurrentDictionary<string, PlaylistInfo> playlists, PulseAnalyticsInfo analytics)
		{
			m_tracks = tracks;
			m_albums = albums;
			m_artists = artists;
			m_playlists = playlists;
			m_analytics = analytics;
		}

		public void Load()
		{
			Stopwatch sw = Stopwatch.StartNew();
			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				LoadArtists(connection);
				LoadAlbums(connection);
				LoadTracks(connection);
				LoadTrackUserScores(connection);
				LoadStarred(connection);
				LoadPlaylists(connection);
				LoadAnalytics(connection);
			}
			finally
			{
				connection.Close();
			}
			sw.Stop();
			Log.Info(-1, "PulseDatabase loaded in " + sw.ElapsedMilliseconds + "ms: "
				+ m_tracks.Count + " tracks, " + m_albums.Count + " albums, "
				+ m_artists.Count + " artists, " + m_playlists.Count + " playlists");
		}

		private void LoadArtists(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT id, name, last_played FROM artists;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				ArtistInfo artist = new ArtistInfo();
				artist.Id = reader.GetString(0);
				artist.Name = reader.GetString(1);
				string lastPlayedStr = reader.GetString(2);
				DateTime lastPlayed;
				if (!string.IsNullOrEmpty(lastPlayedStr) && DateTime.TryParse(lastPlayedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out lastPlayed))
				{
					artist.LastPlayed = lastPlayed;
				}
				m_artists[artist.Id] = artist;
			}
			reader.Close();
		}

		private void LoadAlbums(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT id, name, artist_name, artist_id, genre, cover_art_id, year FROM albums;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				AlbumInfo album = new AlbumInfo();
				album.Id = reader.GetString(0);
				album.Name = reader.GetString(1);
				album.ArtistName = reader.GetString(2);
				album.ArtistId = reader.GetString(3);
				album.Genre = reader.GetString(4);
				album.CoverArtId = reader.GetString(5);
				album.Year = reader.GetInt32(6);
				m_albums[album.Id] = album;
			}
			reader.Close();
		}

		private void LoadTracks(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = @"SELECT id, title, artist, artist_id, album, album_id, genre, file_path,
				cover_art_id, track_number, disc_number, year, duration_seconds, file_size_bytes,
				content_type, suffix, rating, last_played,
				play_count, skip_count, total_listen_seconds, weighted_score FROM tracks;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				TrackInfo track = new TrackInfo();
				track.Id = reader.GetString(0);
				track.Title = reader.GetString(1);
				track.Artist = reader.GetString(2);
				track.ArtistId = reader.GetString(3);
				track.Album = reader.GetString(4);
				track.AlbumId = reader.GetString(5);
				track.Genre = reader.GetString(6);
				track.FilePath = reader.GetString(7);
				track.CoverArtId = reader.GetString(8);
				track.TrackNumber = reader.GetInt32(9);
				track.DiscNumber = reader.GetInt32(10);
				track.Year = reader.GetInt32(11);
				track.DurationSeconds = reader.GetInt32(12);
				track.FileSizeBytes = reader.GetInt64(13);
				track.ContentType = reader.GetString(14);
				track.Suffix = reader.GetString(15);
				track.Rating = reader.GetInt32(16);
				string lastPlayedStr = reader.GetString(17);
				DateTime lastPlayed;
				if (DateTime.TryParse(lastPlayedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out lastPlayed))
				{
					track.LastPlayed = lastPlayed;
				}
				track.Score.PlayCount = reader.GetInt32(18);
				track.Score.SkipCount = reader.GetInt32(19);
				track.Score.TotalListenSeconds = reader.GetDouble(20);
				track.Score.WeightedScore = (float)reader.GetDouble(21);
				m_tracks[track.Id] = track;
			}
			reader.Close();
		}

		private void LoadTrackUserScores(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT track_id, user_name, play_count, skip_count, total_listen_seconds, weighted_score FROM track_user_scores;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				string trackId = reader.GetString(0);
				TrackInfo track;
				if (!m_tracks.TryGetValue(trackId, out track))
				{
					continue;
				}
				string userName = reader.GetString(1);
				ScoreData data = new ScoreData();
				data.PlayCount = reader.GetInt32(2);
				data.SkipCount = reader.GetInt32(3);
				data.TotalListenSeconds = reader.GetDouble(4);
				data.WeightedScore = (float)reader.GetDouble(5);
				track.UserScore[userName] = data;
			}
			reader.Close();
		}

		private void LoadStarred(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT entity_kind, entity_id, user_name, starred FROM starred;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				string kind = reader.GetString(0);
				string id = reader.GetString(1);
				string userName = reader.GetString(2);
				bool starred = reader.GetInt32(3) != 0;

				if (kind == "track")
				{
					TrackInfo track;
					if (m_tracks.TryGetValue(id, out track)) { track.Starred[userName] = starred; }
				}
				else if (kind == "album")
				{
					AlbumInfo album;
					if (m_albums.TryGetValue(id, out album)) { album.Starred[userName] = starred; }
				}
				else if (kind == "artist")
				{
					ArtistInfo artist;
					if (m_artists.TryGetValue(id, out artist)) { artist.Starred[userName] = starred; }
				}
			}
			reader.Close();
		}

		private void LoadPlaylists(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT id, name, comment, duration_seconds, last_played FROM playlists;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				PlaylistInfo playlist = new PlaylistInfo();
				playlist.Id = reader.GetString(0);
				playlist.Name = reader.GetString(1);
				playlist.Comment = reader.GetString(2);
				playlist.DurationSeconds = reader.GetInt64(3);
				string lastPlayedStr = reader.GetString(4);
				DateTime lastPlayed;
				if (!string.IsNullOrEmpty(lastPlayedStr) && DateTime.TryParse(lastPlayedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out lastPlayed))
				{
					playlist.LastPlayed = lastPlayed;
				}
				m_playlists[playlist.Id] = playlist;
			}
			reader.Close();

			SqliteCommand tracksCommand = connection.CreateCommand();
			tracksCommand.CommandText = "SELECT playlist_id, track_id FROM playlist_tracks ORDER BY playlist_id, position;";
			SqliteDataReader tracksReader = tracksCommand.ExecuteReader();
			while (tracksReader.Read())
			{
				string playlistId = tracksReader.GetString(0);
				string trackId = tracksReader.GetString(1);
				PlaylistInfo playlist;
				if (m_playlists.TryGetValue(playlistId, out playlist))
				{
					playlist.TrackIds.Add(trackId);
				}
			}
			tracksReader.Close();

			SqliteCommand userLastPlayedCommand = connection.CreateCommand();
			userLastPlayedCommand.CommandText = "SELECT playlist_id, user_name, last_played FROM playlist_user_last_played;";
			SqliteDataReader userLastPlayedReader = userLastPlayedCommand.ExecuteReader();
			while (userLastPlayedReader.Read())
			{
				string playlistId = userLastPlayedReader.GetString(0);
				string userName = userLastPlayedReader.GetString(1);
				string lastPlayedStr = userLastPlayedReader.GetString(2);
				PlaylistInfo playlist;
				if (!m_playlists.TryGetValue(playlistId, out playlist))
				{
					continue;
				}
				DateTime lastPlayed;
				if (string.IsNullOrEmpty(lastPlayedStr) || !DateTime.TryParse(lastPlayedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out lastPlayed))
				{
					continue;
				}
				playlist.UserLastPlayed[userName] = lastPlayed;
			}
			userLastPlayedReader.Close();
		}

		private void LoadAnalytics(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT track_id FROM analytics_recently_played ORDER BY position;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				m_analytics.RecentlyPlayed.Add(reader.GetString(0));
			}
			reader.Close();
		}

		public void Save(string reason)
		{
			lock (m_saveLock)
			{
				Stopwatch sw = Stopwatch.StartNew();
				SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
				try
				{
					SqliteTransaction transaction = connection.BeginTransaction();
					try
					{
						int artists = SaveDirtyArtists(connection, transaction);
						int albums = SaveDirtyAlbums(connection, transaction);
						int tracks = SaveDirtyTracks(connection, transaction);
						int playlists = SaveDirtyPlaylists(connection, transaction);
						int analytics = SaveAnalytics(connection, transaction);
						transaction.Commit();
						sw.Stop();
						int written = artists + albums + tracks + playlists + analytics;
						if (written > 0)
						{
							Log.Info(-1, "PulseDatabase saved " + written + " dirty rows in " + sw.ElapsedMilliseconds + "ms"
								+ " [" + reason + "]"
								+ " (artists=" + artists + " albums=" + albums + " tracks=" + tracks
								+ " playlists=" + playlists + " analytics=" + analytics + ")");
						}
					}
					catch (Exception ex)
					{
						transaction.Rollback();
						Log.Error(-1, "Save failed, rolled back: " + ex.Message);
						throw;
					}
				}
				finally
				{
					connection.Close();
				}
			}
		}

		private int SaveDirtyArtists(SqliteConnection connection, SqliteTransaction transaction)
		{
			int count = 0;
			foreach (ArtistInfo artist in m_artists.Values)
			{
				if (!artist.m_bIsDirty) { continue; }
				UpsertArtist(connection, transaction, artist);
				WriteStarred(connection, transaction, "artist", artist.Id, artist.Starred);
				artist.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		private int SaveDirtyAlbums(SqliteConnection connection, SqliteTransaction transaction)
		{
			int count = 0;
			foreach (AlbumInfo album in m_albums.Values)
			{
				if (!album.m_bIsDirty) { continue; }
				UpsertAlbum(connection, transaction, album);
				WriteStarred(connection, transaction, "album", album.Id, album.Starred);
				album.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		private int SaveDirtyTracks(SqliteConnection connection, SqliteTransaction transaction)
		{
			int count = 0;
			foreach (TrackInfo track in m_tracks.Values)
			{
				if (!track.m_bIsDirty) { continue; }
				UpsertTrack(connection, transaction, track);
				WriteTrackUserScores(connection, transaction, track);
				WriteStarred(connection, transaction, "track", track.Id, track.Starred);
				track.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		private int SaveDirtyPlaylists(SqliteConnection connection, SqliteTransaction transaction)
		{
			int count = 0;
			foreach (PlaylistInfo playlist in m_playlists.Values)
			{
				if (!playlist.m_bIsDirty) { continue; }
				UpsertPlaylist(connection, transaction, playlist);
				WritePlaylistTracks(connection, transaction, playlist);
				WritePlaylistUserLastPlayed(connection, transaction, playlist);
				playlist.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		public static void UpsertArtist(SqliteConnection connection, SqliteTransaction transaction, ArtistInfo artist)
		{
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = @"INSERT INTO artists (id, name, last_played) VALUES ($id, $name, $last_played)
				ON CONFLICT(id) DO UPDATE SET name = excluded.name, last_played = excluded.last_played;";
			command.Parameters.AddWithValue("$id", artist.Id);
			command.Parameters.AddWithValue("$name", artist.Name ?? "");
			command.Parameters.AddWithValue("$last_played", FormatLastPlayed(artist.LastPlayed));
			command.ExecuteNonQuery();
		}

		private static string FormatLastPlayed(DateTime value)
		{
			if (value == default(DateTime))
			{
				return "";
			}
			return value.ToString("o");
		}

		public static void UpsertAlbum(SqliteConnection connection, SqliteTransaction transaction, AlbumInfo album)
		{
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = @"INSERT INTO albums (id, name, artist_name, artist_id, genre, cover_art_id, year)
				VALUES ($id, $name, $artist_name, $artist_id, $genre, $cover_art_id, $year)
				ON CONFLICT(id) DO UPDATE SET
					name = excluded.name,
					artist_name = excluded.artist_name,
					artist_id = excluded.artist_id,
					genre = excluded.genre,
					cover_art_id = excluded.cover_art_id,
					year = excluded.year;";
			command.Parameters.AddWithValue("$id", album.Id);
			command.Parameters.AddWithValue("$name", album.Name ?? "");
			command.Parameters.AddWithValue("$artist_name", album.ArtistName ?? "");
			command.Parameters.AddWithValue("$artist_id", album.ArtistId ?? "");
			command.Parameters.AddWithValue("$genre", album.Genre ?? "");
			command.Parameters.AddWithValue("$cover_art_id", album.CoverArtId ?? "");
			command.Parameters.AddWithValue("$year", album.Year);
			command.ExecuteNonQuery();
		}

		public static void UpsertTrack(SqliteConnection connection, SqliteTransaction transaction, TrackInfo track)
		{
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = @"INSERT INTO tracks (id, title, artist, artist_id, album, album_id, genre, file_path,
					cover_art_id, track_number, disc_number, year, duration_seconds, file_size_bytes,
					content_type, suffix, rating, last_played,
					play_count, skip_count, total_listen_seconds, weighted_score)
				VALUES ($id, $title, $artist, $artist_id, $album, $album_id, $genre, $file_path,
					$cover_art_id, $track_number, $disc_number, $year, $duration_seconds, $file_size_bytes,
					$content_type, $suffix, $rating, $last_played,
					$play_count, $skip_count, $total_listen_seconds, $weighted_score)
				ON CONFLICT(id) DO UPDATE SET
					title = excluded.title,
					artist = excluded.artist,
					artist_id = excluded.artist_id,
					album = excluded.album,
					album_id = excluded.album_id,
					genre = excluded.genre,
					file_path = excluded.file_path,
					cover_art_id = excluded.cover_art_id,
					track_number = excluded.track_number,
					disc_number = excluded.disc_number,
					year = excluded.year,
					duration_seconds = excluded.duration_seconds,
					file_size_bytes = excluded.file_size_bytes,
					content_type = excluded.content_type,
					suffix = excluded.suffix,
					rating = excluded.rating,
					last_played = excluded.last_played,
					play_count = excluded.play_count,
					skip_count = excluded.skip_count,
					total_listen_seconds = excluded.total_listen_seconds,
					weighted_score = excluded.weighted_score;";
			command.Parameters.AddWithValue("$id", track.Id);
			command.Parameters.AddWithValue("$title", track.Title ?? "");
			command.Parameters.AddWithValue("$artist", track.Artist ?? "");
			command.Parameters.AddWithValue("$artist_id", track.ArtistId ?? "");
			command.Parameters.AddWithValue("$album", track.Album ?? "");
			command.Parameters.AddWithValue("$album_id", track.AlbumId ?? "");
			command.Parameters.AddWithValue("$genre", track.Genre ?? "");
			command.Parameters.AddWithValue("$file_path", track.FilePath ?? "");
			command.Parameters.AddWithValue("$cover_art_id", track.CoverArtId ?? "");
			command.Parameters.AddWithValue("$track_number", track.TrackNumber);
			command.Parameters.AddWithValue("$disc_number", track.DiscNumber);
			command.Parameters.AddWithValue("$year", track.Year);
			command.Parameters.AddWithValue("$duration_seconds", track.DurationSeconds);
			command.Parameters.AddWithValue("$file_size_bytes", track.FileSizeBytes);
			command.Parameters.AddWithValue("$content_type", track.ContentType ?? "");
			command.Parameters.AddWithValue("$suffix", track.Suffix ?? "");
			command.Parameters.AddWithValue("$rating", track.Rating);
			command.Parameters.AddWithValue("$last_played", track.LastPlayed.ToString("o"));
			command.Parameters.AddWithValue("$play_count", track.Score.PlayCount);
			command.Parameters.AddWithValue("$skip_count", track.Score.SkipCount);
			command.Parameters.AddWithValue("$total_listen_seconds", track.Score.TotalListenSeconds);
			command.Parameters.AddWithValue("$weighted_score", track.Score.WeightedScore);
			command.ExecuteNonQuery();
		}

		public static void UpsertPlaylist(SqliteConnection connection, SqliteTransaction transaction, PlaylistInfo playlist)
		{
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = @"INSERT INTO playlists (id, name, comment, duration_seconds, last_played)
				VALUES ($id, $name, $comment, $duration_seconds, $last_played)
				ON CONFLICT(id) DO UPDATE SET
					name = excluded.name,
					comment = excluded.comment,
					duration_seconds = excluded.duration_seconds,
					last_played = excluded.last_played;";
			command.Parameters.AddWithValue("$id", playlist.Id);
			command.Parameters.AddWithValue("$name", playlist.Name ?? "");
			command.Parameters.AddWithValue("$comment", playlist.Comment ?? "");
			command.Parameters.AddWithValue("$duration_seconds", playlist.DurationSeconds);
			command.Parameters.AddWithValue("$last_played", FormatLastPlayed(playlist.LastPlayed));
			command.ExecuteNonQuery();
		}

		public static void WriteTrackUserScores(SqliteConnection connection, SqliteTransaction transaction, TrackInfo track)
		{
			SqliteCommand delete = connection.CreateCommand();
			delete.Transaction = transaction;
			delete.CommandText = "DELETE FROM track_user_scores WHERE track_id = $track_id;";
			delete.Parameters.AddWithValue("$track_id", track.Id);
			delete.ExecuteNonQuery();

			foreach (KeyValuePair<string, ScoreData> entry in track.UserScore)
			{
				SqliteCommand insert = connection.CreateCommand();
				insert.Transaction = transaction;
				insert.CommandText = @"INSERT INTO track_user_scores (track_id, user_name, play_count, skip_count, total_listen_seconds, weighted_score)
					VALUES ($track_id, $user_name, $play_count, $skip_count, $total_listen_seconds, $weighted_score);";
				insert.Parameters.AddWithValue("$track_id", track.Id);
				insert.Parameters.AddWithValue("$user_name", entry.Key);
				insert.Parameters.AddWithValue("$play_count", entry.Value.PlayCount);
				insert.Parameters.AddWithValue("$skip_count", entry.Value.SkipCount);
				insert.Parameters.AddWithValue("$total_listen_seconds", entry.Value.TotalListenSeconds);
				insert.Parameters.AddWithValue("$weighted_score", entry.Value.WeightedScore);
				insert.ExecuteNonQuery();
			}
		}

		public static void WriteStarred(SqliteConnection connection, SqliteTransaction transaction, string entityKind, string entityId, Dictionary<string, bool> starred)
		{
			SqliteCommand delete = connection.CreateCommand();
			delete.Transaction = transaction;
			delete.CommandText = "DELETE FROM starred WHERE entity_kind = $kind AND entity_id = $id;";
			delete.Parameters.AddWithValue("$kind", entityKind);
			delete.Parameters.AddWithValue("$id", entityId);
			delete.ExecuteNonQuery();

			foreach (KeyValuePair<string, bool> entry in starred)
			{
				SqliteCommand insert = connection.CreateCommand();
				insert.Transaction = transaction;
				insert.CommandText = @"INSERT INTO starred (entity_kind, entity_id, user_name, starred)
					VALUES ($kind, $id, $user_name, $starred);";
				insert.Parameters.AddWithValue("$kind", entityKind);
				insert.Parameters.AddWithValue("$id", entityId);
				insert.Parameters.AddWithValue("$user_name", entry.Key);
				insert.Parameters.AddWithValue("$starred", entry.Value ? 1 : 0);
				insert.ExecuteNonQuery();
			}
		}

		public static void WritePlaylistUserLastPlayed(SqliteConnection connection, SqliteTransaction transaction, PlaylistInfo playlist)
		{
			SqliteCommand delete = connection.CreateCommand();
			delete.Transaction = transaction;
			delete.CommandText = "DELETE FROM playlist_user_last_played WHERE playlist_id = $playlist_id;";
			delete.Parameters.AddWithValue("$playlist_id", playlist.Id);
			delete.ExecuteNonQuery();

			foreach (KeyValuePair<string, DateTime> entry in playlist.UserLastPlayed)
			{
				if (entry.Value == default(DateTime))
				{
					continue;
				}
				SqliteCommand insert = connection.CreateCommand();
				insert.Transaction = transaction;
				insert.CommandText = @"INSERT INTO playlist_user_last_played (playlist_id, user_name, last_played)
					VALUES ($playlist_id, $user_name, $last_played);";
				insert.Parameters.AddWithValue("$playlist_id", playlist.Id);
				insert.Parameters.AddWithValue("$user_name", entry.Key);
				insert.Parameters.AddWithValue("$last_played", entry.Value.ToString("o"));
				insert.ExecuteNonQuery();
			}
		}

		public static void WritePlaylistTracks(SqliteConnection connection, SqliteTransaction transaction, PlaylistInfo playlist)
		{
			SqliteCommand delete = connection.CreateCommand();
			delete.Transaction = transaction;
			delete.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = $playlist_id;";
			delete.Parameters.AddWithValue("$playlist_id", playlist.Id);
			delete.ExecuteNonQuery();

			for (int position = 0; position < playlist.TrackIds.Count; position++)
			{
				SqliteCommand insert = connection.CreateCommand();
				insert.Transaction = transaction;
				insert.CommandText = @"INSERT INTO playlist_tracks (playlist_id, position, track_id)
					VALUES ($playlist_id, $position, $track_id);";
				insert.Parameters.AddWithValue("$playlist_id", playlist.Id);
				insert.Parameters.AddWithValue("$position", position);
				insert.Parameters.AddWithValue("$track_id", playlist.TrackIds[position]);
				insert.ExecuteNonQuery();
			}
		}

		private int SaveAnalytics(SqliteConnection connection, SqliteTransaction transaction)
		{
			if (!m_analytics.m_bIsDirty) { return 0; }

			SqliteCommand delete = connection.CreateCommand();
			delete.Transaction = transaction;
			delete.CommandText = "DELETE FROM analytics_recently_played;";
			delete.ExecuteNonQuery();

			for (int position = 0; position < m_analytics.RecentlyPlayed.Count; position++)
			{
				SqliteCommand insert = connection.CreateCommand();
				insert.Transaction = transaction;
				insert.CommandText = "INSERT INTO analytics_recently_played (position, track_id) VALUES ($position, $track_id);";
				insert.Parameters.AddWithValue("$position", position);
				insert.Parameters.AddWithValue("$track_id", m_analytics.RecentlyPlayed[position]);
				insert.ExecuteNonQuery();
			}
			m_analytics.m_bIsDirty = false;
			return 1;
		}

		// SQL transaction that removes a track and any album/artist it
		// orphans (and their starred rows). Mirrors the in-memory cascade
		// already performed by PulseData.RemoveTrack so orphan rows don't
		// survive and reappear on the next Load (#302).
		public void DeleteTrackAndOrphans(string trackId, string albumId, string artistId, bool albumEmptied, bool artistEmptied)
		{
			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteTransaction transaction = connection.BeginTransaction();
				try
				{
					SqliteCommand delScores = connection.CreateCommand();
					delScores.Transaction = transaction;
					delScores.CommandText = "DELETE FROM track_user_scores WHERE track_id = $id;";
					delScores.Parameters.AddWithValue("$id", trackId);
					delScores.ExecuteNonQuery();

					SqliteCommand delStarred = connection.CreateCommand();
					delStarred.Transaction = transaction;
					delStarred.CommandText = "DELETE FROM starred WHERE entity_kind = 'track' AND entity_id = $id;";
					delStarred.Parameters.AddWithValue("$id", trackId);
					delStarred.ExecuteNonQuery();

					SqliteCommand delTrack = connection.CreateCommand();
					delTrack.Transaction = transaction;
					delTrack.CommandText = "DELETE FROM tracks WHERE id = $id;";
					delTrack.Parameters.AddWithValue("$id", trackId);
					delTrack.ExecuteNonQuery();

					if (albumEmptied)
					{
						SqliteCommand delAlbum = connection.CreateCommand();
						delAlbum.Transaction = transaction;
						delAlbum.CommandText = "DELETE FROM albums WHERE id = $id;";
						delAlbum.Parameters.AddWithValue("$id", albumId);
						delAlbum.ExecuteNonQuery();

						SqliteCommand delAlbumStar = connection.CreateCommand();
						delAlbumStar.Transaction = transaction;
						delAlbumStar.CommandText = "DELETE FROM starred WHERE entity_kind = 'album' AND entity_id = $id;";
						delAlbumStar.Parameters.AddWithValue("$id", albumId);
						delAlbumStar.ExecuteNonQuery();
					}

					if (artistEmptied)
					{
						SqliteCommand delArtist = connection.CreateCommand();
						delArtist.Transaction = transaction;
						delArtist.CommandText = "DELETE FROM artists WHERE id = $id;";
						delArtist.Parameters.AddWithValue("$id", artistId);
						delArtist.ExecuteNonQuery();

						SqliteCommand delArtistStar = connection.CreateCommand();
						delArtistStar.Transaction = transaction;
						delArtistStar.CommandText = "DELETE FROM starred WHERE entity_kind = 'artist' AND entity_id = $id;";
						delArtistStar.Parameters.AddWithValue("$id", artistId);
						delArtistStar.ExecuteNonQuery();
					}

					transaction.Commit();
				}
				catch
				{
					transaction.Rollback();
					throw;
				}
			}
			finally
			{
				connection.Close();
			}
		}

		public void DeletePlaylistRows(string playlistId)
		{
			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				// playlist_tracks has FK with ON DELETE CASCADE, so deleting the
				// playlist row clears the join rows too. playlist_user_last_played
				// (v3) has no FK, so its rows must be removed explicitly or they
				// orphan and resurface if the id is reused (#303).
				SqliteCommand delLastPlayed = connection.CreateCommand();
				delLastPlayed.CommandText = "DELETE FROM playlist_user_last_played WHERE playlist_id = $id;";
				delLastPlayed.Parameters.AddWithValue("$id", playlistId);
				delLastPlayed.ExecuteNonQuery();

				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "DELETE FROM playlists WHERE id = $id;";
				command.Parameters.AddWithValue("$id", playlistId);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		// Read-through / write-through: no in-memory cache, no dirty bit. The
		// access pattern is one row per user max for play queue and small
		// per-user lists for bookmarks, so the indirection isn't worth it.
		public PlayQueueInfo GetPlayQueue(string userName)
		{
			PlayQueueInfo result = new PlayQueueInfo();
			if (string.IsNullOrEmpty(userName))
			{
				return result;
			}

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand state = connection.CreateCommand();
				state.CommandText = "SELECT current_track_id, position_ms, changed, changed_by FROM playqueue_state WHERE user_name = $u;";
				state.Parameters.AddWithValue("$u", userName);
				SqliteDataReader stateReader = state.ExecuteReader();
				if (stateReader.Read())
				{
					result.CurrentTrackId = stateReader.GetString(0);
					result.PositionMs = stateReader.GetInt64(1);
					string changedStr = stateReader.GetString(2);
					DateTime changed;
					if (!string.IsNullOrEmpty(changedStr) && DateTime.TryParse(changedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out changed))
					{
						result.Changed = changed;
					}
					result.ChangedBy = stateReader.GetString(3);
				}
				stateReader.Close();

				SqliteCommand entries = connection.CreateCommand();
				entries.CommandText = "SELECT track_id FROM playqueue_entries WHERE user_name = $u ORDER BY position;";
				entries.Parameters.AddWithValue("$u", userName);
				SqliteDataReader entriesReader = entries.ExecuteReader();
				while (entriesReader.Read())
				{
					result.TrackIds.Add(entriesReader.GetString(0));
				}
				entriesReader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public void SavePlayQueue(string userName, List<string> trackIds, string currentTrackId, long positionMs, string changedBy)
		{
			if (string.IsNullOrEmpty(userName))
			{
				return;
			}

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteTransaction transaction = connection.BeginTransaction();
				try
				{
					SqliteCommand clearEntries = connection.CreateCommand();
					clearEntries.Transaction = transaction;
					clearEntries.CommandText = "DELETE FROM playqueue_entries WHERE user_name = $u;";
					clearEntries.Parameters.AddWithValue("$u", userName);
					clearEntries.ExecuteNonQuery();

					if (trackIds != null)
					{
						for (int position = 0; position < trackIds.Count; position++)
						{
							SqliteCommand insertEntry = connection.CreateCommand();
							insertEntry.Transaction = transaction;
							insertEntry.CommandText = "INSERT INTO playqueue_entries (user_name, position, track_id) VALUES ($u, $p, $t);";
							insertEntry.Parameters.AddWithValue("$u", userName);
							insertEntry.Parameters.AddWithValue("$p", position);
							insertEntry.Parameters.AddWithValue("$t", trackIds[position]);
							insertEntry.ExecuteNonQuery();
						}
					}

					SqliteCommand upsertState = connection.CreateCommand();
					upsertState.Transaction = transaction;
					upsertState.CommandText = @"INSERT INTO playqueue_state (user_name, current_track_id, position_ms, changed, changed_by)
						VALUES ($u, $c, $p, $ch, $cb)
						ON CONFLICT(user_name) DO UPDATE SET
							current_track_id = excluded.current_track_id,
							position_ms = excluded.position_ms,
							changed = excluded.changed,
							changed_by = excluded.changed_by;";
					upsertState.Parameters.AddWithValue("$u", userName);
					upsertState.Parameters.AddWithValue("$c", currentTrackId ?? "");
					upsertState.Parameters.AddWithValue("$p", positionMs);
					upsertState.Parameters.AddWithValue("$ch", DateTime.UtcNow.ToString("o"));
					upsertState.Parameters.AddWithValue("$cb", changedBy ?? "");
					upsertState.ExecuteNonQuery();

					transaction.Commit();
				}
				catch
				{
					transaction.Rollback();
					throw;
				}
			}
			finally
			{
				connection.Close();
			}
		}

		public List<BookmarkInfo> GetBookmarks(string userName)
		{
			List<BookmarkInfo> result = new List<BookmarkInfo>();
			if (string.IsNullOrEmpty(userName))
			{
				return result;
			}

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT track_id, position_ms, comment, created, changed FROM bookmarks WHERE user_name = $u ORDER BY changed DESC;";
				command.Parameters.AddWithValue("$u", userName);
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					BookmarkInfo bookmark = new BookmarkInfo();
					bookmark.TrackId = reader.GetString(0);
					bookmark.PositionMs = reader.GetInt64(1);
					bookmark.Comment = reader.GetString(2);
					DateTime created;
					if (DateTime.TryParse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind, out created))
					{
						bookmark.Created = created;
					}
					DateTime changed;
					if (DateTime.TryParse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind, out changed))
					{
						bookmark.Changed = changed;
					}
					result.Add(bookmark);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public void SaveBookmark(string userName, string trackId, long positionMs, string comment)
		{
			if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(trackId))
			{
				return;
			}

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				string nowIso = DateTime.UtcNow.ToString("o");
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"INSERT INTO bookmarks (user_name, track_id, position_ms, comment, created, changed)
					VALUES ($u, $t, $p, $c, $created, $changed)
					ON CONFLICT(user_name, track_id) DO UPDATE SET
						position_ms = excluded.position_ms,
						comment = excluded.comment,
						changed = excluded.changed;";
				command.Parameters.AddWithValue("$u", userName);
				command.Parameters.AddWithValue("$t", trackId);
				command.Parameters.AddWithValue("$p", positionMs);
				command.Parameters.AddWithValue("$c", comment ?? "");
				command.Parameters.AddWithValue("$created", nowIso);
				command.Parameters.AddWithValue("$changed", nowIso);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		public void DeleteBookmark(string userName, string trackId)
		{
			if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(trackId))
			{
				return;
			}

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "DELETE FROM bookmarks WHERE user_name = $u AND track_id = $t;";
				command.Parameters.AddWithValue("$u", userName);
				command.Parameters.AddWithValue("$t", trackId);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		// Append-only INSERT into the v6 analytics_events log. Write-through,
		// one row per event, never updated or deleted (except on user wipe).
		// action and media_type are stored as the enum *names* to match how
		// PulseWire serializes them on the wire. A null/empty media id is
		// dropped -- an event with no subject is not useful to aggregate.
		public void RecordAnalyticsEvent(string userName, PulseAnalytics analytics, DateTime occurredAt)
		{
			if (analytics == null || string.IsNullOrEmpty(analytics.MediaId))
			{
				return;
			}

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"INSERT INTO analytics_events (occurred_at, user_name, action, media_type, media_id)
					VALUES ($occurred, $u, $action, $type, $id);";
				command.Parameters.AddWithValue("$occurred", occurredAt.ToString("o"));
				command.Parameters.AddWithValue("$u", userName ?? "");
				command.Parameters.AddWithValue("$action", analytics.Action.ToString());
				command.Parameters.AddWithValue("$type", analytics.MediaType.ToString());
				command.Parameters.AddWithValue("$id", analytics.MediaId);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		/// <summary>
		/// Rolls up the v6 analytics_events log for one media type. Counts only
		/// 'Started' events (a collection/track being started is the unit of a
		/// "play"); media_type and action are matched against the stored enum
		/// names. When userName is non-empty the rollup is scoped to that user,
		/// otherwise it spans every user. occurred_at is stored round-trip ("o"),
		/// so MAX() yields a sortable timestamp the caller parses back.
		/// </summary>
		public Dictionary<string, AnalyticsAggregate> GetStartedAggregates(string userName, eDataType mediaType)
		{
			Dictionary<string, AnalyticsAggregate> aggregates = new Dictionary<string, AnalyticsAggregate>();
			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				bool scopedToUser = !string.IsNullOrEmpty(userName);
				string userClause = scopedToUser ? " AND user_name = $u" : "";
				command.CommandText = "SELECT media_id, COUNT(*) AS plays, MAX(occurred_at) AS last"
					+ " FROM analytics_events"
					+ " WHERE action = $action AND media_type = $type" + userClause
					+ " GROUP BY media_id;";
				command.Parameters.AddWithValue("$action", PulseAnalytics.eAction.Started.ToString());
				command.Parameters.AddWithValue("$type", mediaType.ToString());
				if (scopedToUser)
				{
					command.Parameters.AddWithValue("$u", userName);
				}

				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					string mediaId = reader.GetString(0);
					if (string.IsNullOrEmpty(mediaId))
					{
						continue;
					}
					AnalyticsAggregate aggregate = new AnalyticsAggregate();
					aggregate.PlayCount = reader.GetInt32(1);
					if (!reader.IsDBNull(2))
					{
						DateTime parsed;
						if (DateTime.TryParse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
						{
							aggregate.LastPlayed = parsed;
						}
					}
					aggregates[mediaId] = aggregate;
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return aggregates;
		}

		// SELECT from the users table. The caller layers per-user counts on top
		// from the in-memory dictionaries.
		public Dictionary<string, UserRecord> ReadAllUsers()
		{
			Dictionary<string, UserRecord> byName = new Dictionary<string, UserRecord>();
			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT name, display_name, created, is_admin FROM users;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					UserRecord record = ReadUserRecord(reader);
					byName[record.Name] = record;
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return byName;
		}

		public UserRecord ReadUser(string name)
		{
			if (string.IsNullOrEmpty(name)) { return null; }
			UserRecord record = null;
			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT name, display_name, created, is_admin FROM users WHERE name = $name;";
				command.Parameters.AddWithValue("$name", name);
				SqliteDataReader reader = command.ExecuteReader();
				if (reader.Read())
				{
					record = ReadUserRecord(reader);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return record;
		}

		private static UserRecord ReadUserRecord(SqliteDataReader reader)
		{
			UserRecord record = new UserRecord();
			record.Name = reader.GetString(0);
			record.DisplayName = reader.GetString(1);
			string createdStr = reader.GetString(2);
			DateTime created;
			if (!string.IsNullOrEmpty(createdStr) && DateTime.TryParse(createdStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out created))
			{
				record.Created = created;
			}
			record.IsAdmin = reader.GetInt32(3) != 0;
			return record;
		}

		public string InsertUser(string name, string displayName, bool isAdmin)
		{
			if (string.IsNullOrWhiteSpace(name)) { return "Name is required."; }

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand exists = connection.CreateCommand();
				exists.CommandText = "SELECT 1 FROM users WHERE name = $name;";
				exists.Parameters.AddWithValue("$name", name);
				object found = exists.ExecuteScalar();
				if (found != null)
				{
					return "A user with that name already exists.";
				}

				SqliteCommand insert = connection.CreateCommand();
				insert.CommandText = "INSERT INTO users (name, display_name, created, is_admin) VALUES ($name, $dn, $created, $admin);";
				insert.Parameters.AddWithValue("$name", name);
				insert.Parameters.AddWithValue("$dn", displayName ?? "");
				insert.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("o"));
				insert.Parameters.AddWithValue("$admin", isAdmin ? 1 : 0);
				insert.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
			return "";
		}

		// Single-transaction update. If `newName` differs from `oldName` the FK
		// columns in every per-user table are also rewritten -- on UNIQUE
		// constraint violation (e.g. a track already had a row for newName)
		// the transaction rolls back and the caller gets an error.
		public string UpdateUserRow(string oldName, string newName, string displayName, bool isAdmin)
		{
			if (string.IsNullOrWhiteSpace(oldName)) { return "Old name is required."; }
			if (string.IsNullOrWhiteSpace(newName)) { return "New name is required."; }

			bool renaming = !string.Equals(oldName, newName, StringComparison.Ordinal);

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand oldExists = connection.CreateCommand();
				oldExists.CommandText = "SELECT 1 FROM users WHERE name = $name;";
				oldExists.Parameters.AddWithValue("$name", oldName);
				if (oldExists.ExecuteScalar() == null)
				{
					return "User not found.";
				}

				if (renaming)
				{
					SqliteCommand newExists = connection.CreateCommand();
					newExists.CommandText = "SELECT 1 FROM users WHERE name = $name;";
					newExists.Parameters.AddWithValue("$name", newName);
					if (newExists.ExecuteScalar() != null)
					{
						return "A user named '" + newName + "' already exists.";
					}
				}

				SqliteTransaction transaction = connection.BeginTransaction();
				try
				{
					SqliteCommand updateUsersRow = connection.CreateCommand();
					updateUsersRow.Transaction = transaction;
					updateUsersRow.CommandText = "UPDATE users SET name = $new, display_name = $dn, is_admin = $admin WHERE name = $old;";
					updateUsersRow.Parameters.AddWithValue("$new", newName);
					updateUsersRow.Parameters.AddWithValue("$dn", displayName ?? "");
					updateUsersRow.Parameters.AddWithValue("$admin", isAdmin ? 1 : 0);
					updateUsersRow.Parameters.AddWithValue("$old", oldName);
					updateUsersRow.ExecuteNonQuery();

					if (renaming)
					{
						string[] tables = new string[] {
							"track_user_scores",
							"starred",
							"playlist_user_last_played",
							"playqueue_state",
							"playqueue_entries",
							"bookmarks",
							"analytics_events"
						};
						for (int index = 0; index < tables.Length; index++)
						{
							SqliteCommand update = connection.CreateCommand();
							update.Transaction = transaction;
							update.CommandText = "UPDATE " + tables[index] + " SET user_name = $new WHERE user_name = $old;";
							update.Parameters.AddWithValue("$new", newName);
							update.Parameters.AddWithValue("$old", oldName);
							update.ExecuteNonQuery();
						}
					}

					transaction.Commit();
				}
				catch (SqliteException ex)
				{
					transaction.Rollback();
					Log.Warning(-1, "UpdateUser '" + oldName + "' -> '" + newName + "' failed: " + ex.Message);
					return "Rename failed -- another row would collide on this name. " + ex.Message;
				}
				catch (Exception ex)
				{
					transaction.Rollback();
					Log.Error(-1, "UpdateUser unexpected error: " + ex.Message);
					throw;
				}
			}
			finally
			{
				connection.Close();
			}

			return "";
		}

		// Wipes the non-cached per-user rows (playqueue + bookmarks +
		// analytics) and the users-table row in a single transaction. The
		// caller scrubs the in-memory dictionaries afterwards.
		public void DeleteUserRows(string userName)
		{
			if (string.IsNullOrEmpty(userName)) { return; }

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteTransaction transaction = connection.BeginTransaction();
				try
				{
					SqliteCommand delPlayQueueEntries = connection.CreateCommand();
					delPlayQueueEntries.Transaction = transaction;
					delPlayQueueEntries.CommandText = "DELETE FROM playqueue_entries WHERE user_name = $u;";
					delPlayQueueEntries.Parameters.AddWithValue("$u", userName);
					delPlayQueueEntries.ExecuteNonQuery();

					SqliteCommand delPlayQueueState = connection.CreateCommand();
					delPlayQueueState.Transaction = transaction;
					delPlayQueueState.CommandText = "DELETE FROM playqueue_state WHERE user_name = $u;";
					delPlayQueueState.Parameters.AddWithValue("$u", userName);
					delPlayQueueState.ExecuteNonQuery();

					SqliteCommand delBookmarks = connection.CreateCommand();
					delBookmarks.Transaction = transaction;
					delBookmarks.CommandText = "DELETE FROM bookmarks WHERE user_name = $u;";
					delBookmarks.Parameters.AddWithValue("$u", userName);
					delBookmarks.ExecuteNonQuery();

					SqliteCommand delAnalytics = connection.CreateCommand();
					delAnalytics.Transaction = transaction;
					delAnalytics.CommandText = "DELETE FROM analytics_events WHERE user_name = $u;";
					delAnalytics.Parameters.AddWithValue("$u", userName);
					delAnalytics.ExecuteNonQuery();

					SqliteCommand delUsersRow = connection.CreateCommand();
					delUsersRow.Transaction = transaction;
					delUsersRow.CommandText = "DELETE FROM users WHERE name = $u;";
					delUsersRow.Parameters.AddWithValue("$u", userName);
					delUsersRow.ExecuteNonQuery();

					transaction.Commit();
				}
				catch (Exception ex)
				{
					transaction.Rollback();
					Log.Error(-1, "DeleteUser failed for '" + userName + "', rolled back: " + ex.Message);
					throw;
				}
			}
			finally
			{
				connection.Close();
			}
		}
	}
}
