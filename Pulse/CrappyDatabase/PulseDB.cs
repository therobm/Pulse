using Microsoft.Data.Sqlite;
using Pulse.DataStorage;
using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Pulse.Database
{
	/// <summary>
	/// Per-(user,item) play counter for one media type: how many 'Started' events
	/// the item has accrued and when it was most recently started. Produced by
	/// <see cref="PulseDB.GetItemStats"/>, backed by the item_stats
	/// table (v7).
	/// </summary>
	public class ItemStats
	{
		public int PlayCount { get; set; }
		public DateTime LastPlayed { get; set; }
	}

	// One row loaded from the track_user_scores join table. PulseData attaches
	// the ScoreData onto the matching TrackData.UserScore after every row has
	// been returned -- the persistence layer never touches the in-memory dicts.
	public class TrackUserScoreRow
	{
		public string TrackId;
		public string UserName;
		public TrackData.ScoreData Score = new TrackData.ScoreData();
	}


	public class StarredRow
	{
		public string EntityKind;
		public string EntityId;
		public string UserName;
		public bool Starred;
	}


	public class TokenRow 
	{
		public string Token;
		public string UserName;
		public string Label;
		public string CreatedAt; //I literally do not give a single fuck about when users were made or used
		public string LastUsed;//I literally do not give a single fuck about when users were made or used

	}


	public class PulseDB
	{
		private object m_saveLock = new object();

		public List<ArtistData> LoadArtists()
		{
			List<ArtistData> result = new List<ArtistData>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, name, last_played FROM artists;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					ArtistData artist = new ArtistData();
					artist.Id = reader.GetString(0);
					artist.Name = reader.GetString(1);
					string lastPlayedStr = reader.GetString(2);
					DateTime lastPlayed;
					if (!string.IsNullOrEmpty(lastPlayedStr) && DateTime.TryParse(lastPlayedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out lastPlayed))
					{
						artist.LastPlayed = lastPlayed;
					}
					result.Add(artist);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public List<AlbumData> LoadAlbums()
		{
			List<AlbumData> result = new List<AlbumData>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, name, artist_name, artist_id, genre, cover_art_id, year FROM albums;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					AlbumData album = new AlbumData();
					album.Id = reader.GetString(0);
					album.Name = reader.GetString(1);
					album.ArtistName = reader.GetString(2);
					album.ArtistId = reader.GetString(3);
					album.Genre = reader.GetString(4);
					album.CoverArtId = reader.GetString(5);
					album.Year = reader.GetInt32(6);
					result.Add(album);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public List<TrackData> LoadTracks()
		{
			List<TrackData> result = new List<TrackData>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"SELECT id, title, artist, artist_id, album, album_id, genre, file_path,
					cover_art_id, track_number, disc_number, year, duration_seconds, file_size_bytes,
					content_type, suffix, rating, last_played,
					play_count, skip_count, total_listen_seconds, weighted_score FROM tracks;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					TrackData track = new TrackData();
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


					//Hack to capture legacy IDs so we can repair playlist links
					if (track.FilePath.Contains("Music"))
					{
						string oldPathRoot = "\\\\192.168.5.4\\Vault\\Music";
						string newPathRoot = "\\\\192.168.5.4\\Vault\\Pulse\\Music";

						string oldFilepath = track.FilePath.Replace(newPathRoot, oldPathRoot);
						track.LegacyId = MusicManager.GenerateID(oldFilepath);
					}

					result.Add(track);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public List<TrackUserScoreRow> LoadTrackUserScores()
		{
			List<TrackUserScoreRow> result = new List<TrackUserScoreRow>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT track_id, user_name, play_count, skip_count, total_listen_seconds, weighted_score FROM track_user_scores;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					TrackUserScoreRow row = new TrackUserScoreRow();
					row.TrackId = reader.GetString(0);
					row.UserName = reader.GetString(1);
					row.Score.PlayCount = reader.GetInt32(2);
					row.Score.SkipCount = reader.GetInt32(3);
					row.Score.TotalListenSeconds = reader.GetDouble(4);
					row.Score.WeightedScore = (float)reader.GetDouble(5);
					result.Add(row);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public List<StarredRow> LoadStarred()
		{
			List<StarredRow> result = new List<StarredRow>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT entity_kind, entity_id, user_name, starred FROM starred;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					StarredRow row = new StarredRow();
					row.EntityKind = reader.GetString(0);
					row.EntityId = reader.GetString(1);
					row.UserName = reader.GetString(2);
					row.Starred = reader.GetInt32(3) != 0;
					result.Add(row);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public List<PlaylistData> LoadPlaylists()
		{
			List<PlaylistData> result = new List<PlaylistData>();
			Dictionary<string, PlaylistData> byId = new Dictionary<string, PlaylistData>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, name, comment, duration_seconds, last_played FROM playlists;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					PlaylistData playlist = new PlaylistData();
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
					result.Add(playlist);
					byId[playlist.Id] = playlist;
				}
				reader.Close();

				SqliteCommand tracksCommand = connection.CreateCommand();
				tracksCommand.CommandText = "SELECT playlist_id, track_id FROM playlist_tracks ORDER BY playlist_id, position;";
				SqliteDataReader tracksReader = tracksCommand.ExecuteReader();
				while (tracksReader.Read())
				{
					string playlistId = tracksReader.GetString(0);
					string trackId = tracksReader.GetString(1);
					PlaylistData playlist;
					if (byId.TryGetValue(playlistId, out playlist))
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
					PlaylistData playlist;
					if (!byId.TryGetValue(playlistId, out playlist))
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
			finally
			{
				connection.Close();
			}
			return result;
		}

		public List<string> LoadRecentlyPlayed()
		{
			List<string> result = new List<string>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT track_id FROM analytics_recently_played ORDER BY position;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					result.Add(reader.GetString(0));
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public void Save(string reason, List<ArtistData> dirtyArtists, List<AlbumData> dirtyAlbums, List<TrackData> dirtyTracks, List<PlaylistData> dirtyPlaylists, PulseAnalyticsData analytics)
		{
			lock (m_saveLock)
			{
				Stopwatch sw = Stopwatch.StartNew();
				SqliteConnection connection = PulseDBConnector.OpenConnection();
				try
				{
					SqliteTransaction transaction = connection.BeginTransaction();
					try
					{
						int artists = SaveDirtyArtists(connection, transaction, dirtyArtists);
						int albums = SaveDirtyAlbums(connection, transaction, dirtyAlbums);
						int tracks = SaveDirtyTracks(connection, transaction, dirtyTracks);
						int playlists = SaveDirtyPlaylists(connection, transaction, dirtyPlaylists);
						int analyticsWritten = SaveAnalytics(connection, transaction, analytics);
						transaction.Commit();
						sw.Stop();
						int written = artists + albums + tracks + playlists + analyticsWritten;
						if (written > 0)
						{
							Log.Info(-1, "PulseDatabase saved " + written + " dirty rows in " + sw.ElapsedMilliseconds + "ms"
								+ " [" + reason + "]"
								+ " (artists=" + artists + " albums=" + albums + " tracks=" + tracks
								+ " playlists=" + playlists + " analytics=" + analyticsWritten + ")");
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

		private int SaveDirtyArtists(SqliteConnection connection, SqliteTransaction transaction, List<ArtistData> dirtyArtists)
		{
			int count = 0;
			for (int index = 0; index < dirtyArtists.Count; index++)
			{
				ArtistData artist = dirtyArtists[index];
				UpdateArtist(connection, transaction, artist);
				WriteStarred(connection, transaction, "artist", artist.Id, artist.Starred);
				artist.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		private int SaveDirtyAlbums(SqliteConnection connection, SqliteTransaction transaction, List<AlbumData> dirtyAlbums)
		{
			int count = 0;
			for (int index = 0; index < dirtyAlbums.Count; index++)
			{
				AlbumData album = dirtyAlbums[index];
				UpdateAlbum(connection, transaction, album);
				WriteStarred(connection, transaction, "album", album.Id, album.Starred);
				album.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		private int SaveDirtyTracks(SqliteConnection connection, SqliteTransaction transaction, List<TrackData> dirtyTracks)
		{
			int count = 0;
			for (int index = 0; index < dirtyTracks.Count; index++)
			{
				TrackData track = dirtyTracks[index];
				UpdateTrack(connection, transaction, track);
				WriteTrackUserScores(connection, transaction, track);
				WriteStarred(connection, transaction, "track", track.Id, track.Starred);
				track.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		private int SaveDirtyPlaylists(SqliteConnection connection, SqliteTransaction transaction, List<PlaylistData> dirtyPlaylists)
		{
			int count = 0;
			for (int index = 0; index < dirtyPlaylists.Count; index++)
			{
				PlaylistData playlist = dirtyPlaylists[index];
				UpdatePlaylist(connection, transaction, playlist);
				WritePlaylistTracks(connection, transaction, playlist);
				WritePlaylistUserLastPlayed(connection, transaction, playlist);
				playlist.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		private int SaveAnalytics(SqliteConnection connection, SqliteTransaction transaction, PulseAnalyticsData analytics)
		{
			if (analytics == null) { return 0; }
			if (!analytics.m_bIsDirty) { return 0; }

			SqliteCommand delete = connection.CreateCommand();
			delete.Transaction = transaction;
			delete.CommandText = "DELETE FROM analytics_recently_played;";
			delete.ExecuteNonQuery();

			for (int position = 0; position < analytics.RecentlyPlayed.Count; position++)
			{
				SqliteCommand insert = connection.CreateCommand();
				insert.Transaction = transaction;
				insert.CommandText = "INSERT INTO analytics_recently_played (position, track_id) VALUES ($position, $track_id);";
				insert.Parameters.AddWithValue("$position", position);
				insert.Parameters.AddWithValue("$track_id", analytics.RecentlyPlayed[position]);
				insert.ExecuteNonQuery();
			}
			analytics.m_bIsDirty = false;
			return 1;
		}

		public static void UpdateArtist(SqliteConnection connection, SqliteTransaction transaction, ArtistData artist)
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
			if (value == default)
			{
				return "";
			}
			return value.ToString("o");
		}

		public static void UpdateAlbum(SqliteConnection connection, SqliteTransaction transaction, AlbumData album)
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

		public static void UpdateTrack(SqliteConnection connection, SqliteTransaction transaction, TrackData track)
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

		public static void UpdatePlaylist(SqliteConnection connection, SqliteTransaction transaction, PlaylistData playlist)
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

		public static void WriteTrackUserScores(SqliteConnection connection, SqliteTransaction transaction, TrackData track)
		{
			SqliteCommand delete = connection.CreateCommand();
			delete.Transaction = transaction;
			delete.CommandText = "DELETE FROM track_user_scores WHERE track_id = $track_id;";
			delete.Parameters.AddWithValue("$track_id", track.Id);
			delete.ExecuteNonQuery();

			foreach (KeyValuePair<string, TrackData.ScoreData> entry in track.UserScore)
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

		public static void WritePlaylistUserLastPlayed(SqliteConnection connection, SqliteTransaction transaction, PlaylistData playlist)
		{
			SqliteCommand delete = connection.CreateCommand();
			delete.Transaction = transaction;
			delete.CommandText = "DELETE FROM playlist_user_last_played WHERE playlist_id = $playlist_id;";
			delete.Parameters.AddWithValue("$playlist_id", playlist.Id);
			delete.ExecuteNonQuery();

			foreach (KeyValuePair<string, DateTime> entry in playlist.UserLastPlayed)
			{
				if (entry.Value == default)
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

		public static void WritePlaylistTracks(SqliteConnection connection, SqliteTransaction transaction, PlaylistData playlist)
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

		// Cleans up SQLite rows for a removed track. PulseData decides which of
		// the album / artist rows have been emptied (their last child went) and
		// passes those flags in -- the persistence layer has no view of the
		// in-memory cascade.
		public void DeleteTrackRows(string trackId, string albumId, string artistId, bool deleteAlbum, bool deleteArtist)
		{
			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

					if (deleteAlbum)
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

					if (deleteArtist)
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
			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

					SqliteCommand UpdateState = connection.CreateCommand();
					UpdateState.Transaction = transaction;
					UpdateState.CommandText = @"INSERT INTO playqueue_state (user_name, current_track_id, position_ms, changed, changed_by)
						VALUES ($u, $c, $p, $ch, $cb)
						ON CONFLICT(user_name) DO UPDATE SET
							current_track_id = excluded.current_track_id,
							position_ms = excluded.position_ms,
							changed = excluded.changed,
							changed_by = excluded.changed_by;";
					UpdateState.Parameters.AddWithValue("$u", userName);
					UpdateState.Parameters.AddWithValue("$c", currentTrackId ?? "");
					UpdateState.Parameters.AddWithValue("$p", positionMs);
					UpdateState.Parameters.AddWithValue("$ch", DateTime.UtcNow.ToString("o"));
					UpdateState.Parameters.AddWithValue("$cb", changedBy ?? "");
					UpdateState.ExecuteNonQuery();

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

			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

		// Append-only INSERT into the playback_events log -- the raw immutable
		// event stream (one row per Started/Paused/Skipped/Completed). On a
		// Started event we additionally Update the item_stats counter so the
		// most-played shelves read a single row per (user, item) rather than
		// re-aggregating the log on every request. Same connection for both
		// statements; log insert first so the counter never gets ahead of the
		// log. A null/empty media id is dropped -- an event with no subject is
		// not useful to count.
		public void RecordPlaybackEvent(string userName, PulseAnalytics analytics, DateTime occurredAt)
		{
			if (analytics == null || string.IsNullOrEmpty(analytics.MediaId))
			{
				return;
			}

			string normalizedUser = userName ?? "";
			string occurredIso = occurredAt.ToString("o");
			string actionName = analytics.Action.ToString();
			string typeName = analytics.MediaType.ToString();

			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand logCommand = connection.CreateCommand();
				logCommand.CommandText = @"INSERT INTO playback_events (occurred_at, user_name, action, media_type, media_id)
					VALUES ($occurred, $u, $action, $type, $id);";
				logCommand.Parameters.AddWithValue("$occurred", occurredIso);
				logCommand.Parameters.AddWithValue("$u", normalizedUser);
				logCommand.Parameters.AddWithValue("$action", actionName);
				logCommand.Parameters.AddWithValue("$type", typeName);
				logCommand.Parameters.AddWithValue("$id", analytics.MediaId);
				logCommand.ExecuteNonQuery();

				if (analytics.Action == PulseAnalytics.eAction.Started)
				{
					SqliteCommand UpdateCommand = connection.CreateCommand();
					UpdateCommand.CommandText = @"INSERT INTO item_stats (user_name, media_type, media_id, play_count, last_played)
						VALUES ($u, $type, $id, 1, $occurred)
						ON CONFLICT(user_name, media_type, media_id) DO UPDATE SET
							play_count = play_count + 1,
							last_played = CASE WHEN excluded.last_played > last_played THEN excluded.last_played ELSE last_played END;";
					UpdateCommand.Parameters.AddWithValue("$u", normalizedUser);
					UpdateCommand.Parameters.AddWithValue("$type", typeName);
					UpdateCommand.Parameters.AddWithValue("$id", analytics.MediaId);
					UpdateCommand.Parameters.AddWithValue("$occurred", occurredIso);
					UpdateCommand.ExecuteNonQuery();
				}
			}
			finally
			{
				connection.Close();
			}
		}

		/// <summary>
		/// Reads the item_stats counter for one media type. Each row already holds
		/// a Started count and the most-recent occurred_at -- no log aggregation
		/// at query time. When userName is non-empty the read is scoped to that
		/// user's counters; otherwise the per-user counters are summed across
		/// every user (and last_played taken as MAX). last_played is round-trip
		/// "o" so the caller parses it back. Empty media_id rows are skipped.
		/// </summary>
		public Dictionary<string, ItemStats> GetItemStats(string userName, ePulseWireType mediaType)
		{
			Dictionary<string, ItemStats> stats = new Dictionary<string, ItemStats>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				bool scopedToUser = !string.IsNullOrEmpty(userName);
				if (scopedToUser)
				{
					command.CommandText = "SELECT media_id, play_count, last_played FROM item_stats"
						+ " WHERE media_type = $type AND user_name = $u;";
					command.Parameters.AddWithValue("$u", userName);
				}
				else
				{
					command.CommandText = "SELECT media_id, SUM(play_count), MAX(last_played) FROM item_stats"
						+ " WHERE media_type = $type GROUP BY media_id;";
				}
				command.Parameters.AddWithValue("$type", mediaType.ToString());

				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					string mediaId = reader.GetString(0);
					if (string.IsNullOrEmpty(mediaId))
					{
						continue;
					}
					ItemStats entry = new ItemStats();
					if (!reader.IsDBNull(1))
					{
						entry.PlayCount = reader.GetInt32(1);
					}
					if (!reader.IsDBNull(2))
					{
						string lastPlayedStr = reader.GetString(2);
						if (!string.IsNullOrEmpty(lastPlayedStr))
						{
							DateTime parsed;
							if (DateTime.TryParse(lastPlayedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
							{
								entry.LastPlayed = parsed;
							}
						}
					}
					stats[mediaId] = entry;
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return stats;
		}

		// Returns one record per row in the users table. The per-user counts
		// (ScoredTrackCount / StarredCount / PlaylistLastPlayedCount) are
		// layered on by PulseData from its in-memory state -- the persistence
		// layer has no view of them.
		public List<UserRecord> ReadAllUsers()
		{
			List<UserRecord> result = new List<UserRecord>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT name, display_name, created, is_admin FROM users;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					result.Add(ReadUserRecord(reader));
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public UserRecord ReadUser(string name)
		{
			if (string.IsNullOrEmpty(name)) { return null; }
			UserRecord record = null;
			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

			SqliteConnection connection = PulseDBConnector.OpenConnection();
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
							"playback_events",
							"item_stats"
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

		// Single-transaction wipe of every SQL row that mentions this user --
		// users table plus the non-cached per-user tables (playqueue, bookmarks,
		// playback_events, item_stats). PulseData runs the in-memory cascade and
		// flushes the resulting dirty rows separately.
		public void DeleteUserRows(string userName)
		{
			if (string.IsNullOrEmpty(userName)) { return; }

			SqliteConnection connection = PulseDBConnector.OpenConnection();
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

					SqliteCommand delPlaybackEvents = connection.CreateCommand();
					delPlaybackEvents.Transaction = transaction;
					delPlaybackEvents.CommandText = "DELETE FROM playback_events WHERE user_name = $u;";
					delPlaybackEvents.Parameters.AddWithValue("$u", userName);
					delPlaybackEvents.ExecuteNonQuery();

					SqliteCommand delItemStats = connection.CreateCommand();
					delItemStats.Transaction = transaction;
					delItemStats.CommandText = "DELETE FROM item_stats WHERE user_name = $u;";
					delItemStats.Parameters.AddWithValue("$u", userName);
					delItemStats.ExecuteNonQuery();

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

		/// <summary>
		/// Returns the stored BCrypt password hash for a user, or "" if the
		/// user has no password set or does not exist. Callers must NOT log
		/// the returned value -- it is verification material.
		/// </summary>
		public string ReadUserPasswordHash(string name)
		{
			if (string.IsNullOrEmpty(name)) { return ""; }
			string hash = "";
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT password_hash FROM users WHERE name = $name;";
				command.Parameters.AddWithValue("$name", name);
				object result = command.ExecuteScalar();
				if (result != null && result != DBNull.Value)
				{
					hash = (string)result;
				}
			}
			finally
			{
				connection.Close();
			}
			return hash;
		}

		/// <summary>
		/// Overwrites a user's stored password hash. The caller has already
		/// hashed the plaintext (BCrypt); this routine never sees it.
		/// </summary>
		public void SetUserPassword(string name, string passwordHash)
		{
			if (string.IsNullOrEmpty(name)) { return; }
			string storedHash = passwordHash;
			if (storedHash == null) { storedHash = ""; }
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "UPDATE users SET password_hash = $hash WHERE name = $name;";
				command.Parameters.AddWithValue("$hash", storedHash);
				command.Parameters.AddWithValue("$name", name);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		/// <summary>
		/// True when at least one user in the table has a non-empty password
		/// hash. The setPassword endpoint uses this to allow the very first
		/// password to be set without a session, then locks the route to
		/// authenticated callers from that point forward.
		/// </summary>
		public bool AnyUserHasPassword()
		{
			bool any = false;
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT EXISTS(SELECT 1 FROM users WHERE password_hash <> '');";
				object result = command.ExecuteScalar();
				if (result != null && result != DBNull.Value)
				{
					any = Convert.ToInt32(result) != 0;
				}
			}
			finally
			{
				connection.Close();
			}
			return any;
		}

		/// <summary>
		/// Inserts a freshly-minted device token. created_at is stamped here so
		/// every caller writes a consistent format; last_used picks up the
		/// schema default ('') until the validation helper bumps it.
		/// </summary>
		public void InsertToken(string token, string userName, string label)
		{
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "INSERT INTO tokens (token, user_name, label, created_at) VALUES ($token, $user_name, $label, $created_at);";
				command.Parameters.AddWithValue("$token", token);
				command.Parameters.AddWithValue("$user_name", userName);
				command.Parameters.AddWithValue("$label", label ?? "");
				command.Parameters.AddWithValue("$created_at", DateTime.UtcNow.ToString("o"));
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		/// <summary>
		/// Returns every row in the tokens table, newest first.
		/// </summary>
		public List<TokenRow> GetAllTokens()
		{
			List<TokenRow> result = new List<TokenRow>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT token, user_name, label, created_at, last_used FROM tokens ORDER BY created_at DESC;";
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					TokenRow row = new TokenRow();
					row.Token = reader.GetString(0);
					row.UserName = reader.GetString(1);
					row.Label = reader.GetString(2);
					row.CreatedAt = reader.GetString(3);
					row.LastUsed = reader.GetString(4);
					result.Add(row);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		/// <summary>
		/// Returns the tokens registered to one user, newest first.
		/// </summary>
		public List<TokenRow> GetTokensForUser(string userName)
		{
			List<TokenRow> result = new List<TokenRow>();
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT token, user_name, label, created_at, last_used FROM tokens WHERE user_name = $user_name ORDER BY created_at DESC;";
				command.Parameters.AddWithValue("$user_name", userName);
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					TokenRow row = new TokenRow();
					row.Token = reader.GetString(0);
					row.UserName = reader.GetString(1);
					row.Label = reader.GetString(2);
					row.CreatedAt = reader.GetString(3);
					row.LastUsed = reader.GetString(4);
					result.Add(row);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		/// <summary>
		/// Resolves a raw token value to its owning user. Returns "" when the
		/// token is not in the table -- the validation helper treats that as
		/// "no auth" without further error handling.
		/// </summary>
		public string LookupTokenUser(string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return "";
			}
			string userName = "";
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT user_name FROM tokens WHERE token = $token;";
				command.Parameters.AddWithValue("$token", token);
				object result = command.ExecuteScalar();
				if (result != null && result != DBNull.Value)
				{
					userName = (string)result;
				}
			}
			finally
			{
				connection.Close();
			}
			return userName;
		}

		/// <summary>
		/// Stamps the last_used column on a token. Called on every successful
		/// token resolve so the management UI can show recent activity.
		/// </summary>
		public void UpdateTokenLastUsed(string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return;
			}
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "UPDATE tokens SET last_used = $last_used WHERE token = $token;";
				command.Parameters.AddWithValue("$last_used", DateTime.UtcNow.ToString("o"));
				command.Parameters.AddWithValue("$token", token);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		/// <summary>
		/// Removes a device token. Idempotent; deleting an unknown token is a
		/// no-op.
		/// </summary>
		public void DeleteToken(string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return;
			}
			SqliteConnection connection = PulseDBConnector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "DELETE FROM tokens WHERE token = $token;";
				command.Parameters.AddWithValue("$token", token);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}
	}
}
