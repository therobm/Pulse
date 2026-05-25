using Microsoft.Data.Sqlite;
using Pulse.Database;
using Pulse.MusicLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Pulse.Data
{
	/// <summary>
	/// SQLite-backed implementation of IPulseDatabase. Replaces the file-per-record
	/// approach in PulseFileDatabase. Same in-memory model (PulseDatabaseBase
	/// dictionaries) as before -- SQLite is the persistence layer only. Reads
	/// hit the dicts; writes flip m_bIsDirty and are flushed to SQLite on Save().
	///
	/// Migration path: see Database/Migrations.cs. Add a new MigrationStep to
	/// evolve the schema; never edit a shipped one.
	/// </summary>
	public class PulseSqliteDatabase : PulseDatabaseBase
	{
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
				WireUpReferences();
				CalculateArtistScores();
			}
			finally
			{
				connection.Close();
			}
			sw.Stop();
			Log.Info(-1, "PulseSqliteDatabase loaded in " + sw.ElapsedMilliseconds + "ms: "
				+ m_tracks.Count + " tracks, " + m_albums.Count + " albums, "
				+ m_artists.Count + " artists, " + m_playlists.Count + " playlists");
		}

		private void LoadArtists(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT id, name FROM artists;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				ArtistInfo artist = new ArtistInfo();
				artist.Id = reader.GetString(0);
				artist.Name = reader.GetString(1);
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
			command.CommandText = "SELECT id, name, comment, duration_seconds FROM playlists;";
			SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				PlaylistInfo playlist = new PlaylistInfo();
				playlist.Id = reader.GetString(0);
				playlist.Name = reader.GetString(1);
				playlist.Comment = reader.GetString(2);
				playlist.DurationSeconds = reader.GetInt64(3);
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

		/// <summary>
		/// Wire AlbumInfo.Tracks and ArtistInfo.Albums lists from the foreign-key
		/// columns now that all rows are loaded. Mirrors PulseFileDatabase's
		/// post-load wireup.
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
		/// Mirrors PulseFileDatabase.CalculateArtistScores; without this the
		/// popular-artists sort and the popular carousel see all zeros.
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

		public override void Save()
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
						int written = 0;
						written += SaveDirtyArtists(connection, transaction);
						written += SaveDirtyAlbums(connection, transaction);
						written += SaveDirtyTracks(connection, transaction);
						written += SaveDirtyPlaylists(connection, transaction);
						SaveAnalytics(connection, transaction);
						transaction.Commit();
						sw.Stop();
						if (written > 0)
						{
							Log.Info(-1, "PulseSqliteDatabase saved " + written + " dirty rows in " + sw.ElapsedMilliseconds + "ms");
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
				playlist.m_bIsDirty = false;
				count++;
			}
			return count;
		}

		// ------- UPSERTs -------

		public static void UpsertArtist(SqliteConnection connection, SqliteTransaction transaction, ArtistInfo artist)
		{
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = @"INSERT INTO artists (id, name) VALUES ($id, $name)
				ON CONFLICT(id) DO UPDATE SET name = excluded.name;";
			command.Parameters.AddWithValue("$id", artist.Id);
			command.Parameters.AddWithValue("$name", artist.Name ?? "");
			command.ExecuteNonQuery();
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
			command.CommandText = @"INSERT INTO playlists (id, name, comment, duration_seconds)
				VALUES ($id, $name, $comment, $duration_seconds)
				ON CONFLICT(id) DO UPDATE SET
					name = excluded.name,
					comment = excluded.comment,
					duration_seconds = excluded.duration_seconds;";
			command.Parameters.AddWithValue("$id", playlist.Id);
			command.Parameters.AddWithValue("$name", playlist.Name ?? "");
			command.Parameters.AddWithValue("$comment", playlist.Comment ?? "");
			command.Parameters.AddWithValue("$duration_seconds", playlist.DurationSeconds);
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

		private void SaveAnalytics(SqliteConnection connection, SqliteTransaction transaction)
		{
			if (!m_analytics.m_bIsDirty) { return; }

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
		}

		// Override the base RemoveTrack to also clean up SQLite. The base operation
		// only updates dicts; we need to delete the track row (and dependent rows
		// via CASCADE / explicit cleanup).
		public override bool RemoveTrack(string trackId)
		{
			bool removed = base.RemoveTrack(trackId);
			if (!removed) { return false; }

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
			return true;
		}

		public override void DeletePlaylist(string playlistId)
		{
			base.DeletePlaylist(playlistId);

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				// playlist_tracks has FK with ON DELETE CASCADE, so deleting the
				// playlist row clears the join rows too.
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
	}
}
