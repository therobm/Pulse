using Microsoft.Data.Sqlite;
using Pulse.Database;
using Pulse.MusicLibrary;
using System;
using System.Diagnostics;
using System.IO;

namespace Pulse.Data
{
	/// <summary>
	/// One-shot importer: if a JSON tree exists at the old PulseFileDatabase
	/// location but the SQLite DB is empty (post-migration), load the JSON tree
	/// in-memory using PulseFileDatabase, then write every entity into SQLite.
	/// On success, rename the JSON tree to {original}.imported_TIMESTAMP/ so we
	/// don't import twice but the data stays on disk as a safety net.
	///
	/// Idempotent-ish: if SQLite already has rows, we skip and don't touch the
	/// JSON tree.
	/// </summary>
	public static class PulseSqliteImporter
	{
		public static void ImportIfNeeded(string jsonRootPath, MusicManager musicManager)
		{
			if (!Directory.Exists(jsonRootPath))
			{
				return;
			}
			if (!HasJsonContent(jsonRootPath))
			{
				return;
			}
			if (SqliteHasContent())
			{
				Log.Info(-1, "SQLite already populated; skipping JSON import (JSON tree at " + jsonRootPath + " left in place).");
				return;
			}

			Log.Info(-1, "Importing JSON DB at " + jsonRootPath + " into SQLite...");
			Stopwatch sw = Stopwatch.StartNew();

			PulseFileDatabase fileDb = new PulseFileDatabase(jsonRootPath, musicManager);
			fileDb.Load();

			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteTransaction transaction = connection.BeginTransaction();
				try
				{
					ImportArtists(connection, transaction, fileDb);
					ImportAlbums(connection, transaction, fileDb);
					ImportTracks(connection, transaction, fileDb);
					ImportPlaylists(connection, transaction, fileDb);
					ImportAnalytics(connection, transaction, fileDb);
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

			sw.Stop();
			Log.Info(-1, "Import complete in " + sw.ElapsedMilliseconds + "ms. Tracks=" + fileDb.GetTrackCount()
				+ " Albums=" + fileDb.GetAlbumCount() + " Artists=" + fileDb.GetArtistCount());

			RenameImportedJsonTree(jsonRootPath);
		}

		private static bool HasJsonContent(string jsonRootPath)
		{
			string tracksDir = Path.Combine(jsonRootPath, "tracks");
			if (Directory.Exists(tracksDir) && Directory.GetFiles(tracksDir, "*.json").Length > 0)
			{
				return true;
			}
			string analytics = Path.Combine(jsonRootPath, "analytics.json");
			if (File.Exists(analytics))
			{
				return true;
			}
			return false;
		}

		private static bool SqliteHasContent()
		{
			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT (SELECT COUNT(*) FROM tracks) + (SELECT COUNT(*) FROM playlists) + (SELECT COUNT(*) FROM artists);";
				object result = command.ExecuteScalar();
				return Convert.ToInt32(result) > 0;
			}
			finally
			{
				connection.Close();
			}
		}

		private static void ImportArtists(SqliteConnection connection, SqliteTransaction transaction, PulseFileDatabase fileDb)
		{
			foreach (ArtistInfo artist in fileDb.GetAllArtists())
			{
				PulseSqliteDatabase.UpsertArtist(connection, transaction, artist);
				PulseSqliteDatabase.WriteStarred(connection, transaction, "artist", artist.Id, artist.Starred);
			}
		}

		private static void ImportAlbums(SqliteConnection connection, SqliteTransaction transaction, PulseFileDatabase fileDb)
		{
			foreach (AlbumInfo album in fileDb.GetAllAlbums())
			{
				PulseSqliteDatabase.UpsertAlbum(connection, transaction, album);
				PulseSqliteDatabase.WriteStarred(connection, transaction, "album", album.Id, album.Starred);
			}
		}

		private static void ImportTracks(SqliteConnection connection, SqliteTransaction transaction, PulseFileDatabase fileDb)
		{
			foreach (TrackInfo track in fileDb.GetAllTracks())
			{
				PulseSqliteDatabase.UpsertTrack(connection, transaction, track);
				PulseSqliteDatabase.WriteTrackUserScores(connection, transaction, track);
				PulseSqliteDatabase.WriteStarred(connection, transaction, "track", track.Id, track.Starred);
			}
		}

		private static void ImportPlaylists(SqliteConnection connection, SqliteTransaction transaction, PulseFileDatabase fileDb)
		{
			// Skip the runtime smart playlists -- those rebuild from track scores
			// on demand and don't belong in persistent storage. We get just the
			// real user playlists by passing null userName (RebuildSmartPlaylists
			// runs but adds to m_autoPlaylists, not m_playlists).
			// We don't actually need to filter -- fileDb.GetAllPlaylists returns
			// both real and auto, but only the m_playlists ones came from disk
			// and have a stable id we care about. Easiest: only iterate real
			// playlists by going through the file DB's persisted set.
			System.Collections.Generic.List<PlaylistInfo> all = fileDb.GetAllPlaylists(null);
			for (int idx = 0; idx < all.Count; idx++)
			{
				PlaylistInfo playlist = all[idx];
				// Smart playlists start with "smart/" in their id-derivation path.
				// Filter by checking for the auto-playlist marker if you have one;
				// for now, persist anything that doesn't look auto-generated.
				if (IsAutoPlaylist(playlist))
				{
					continue;
				}
				PulseSqliteDatabase.UpsertPlaylist(connection, transaction, playlist);
				PulseSqliteDatabase.WritePlaylistTracks(connection, transaction, playlist);
			}
		}

		private static bool IsAutoPlaylist(PlaylistInfo playlist)
		{
			// Smart playlists are constructed in PulseDatabaseBase.RebuildSmartPlaylist
			// with names like "Top Rated (UserName)". Cheap heuristic: name starts
			// with "Top Rated (". If the smart-playlist generation changes, update
			// this filter too.
			if (playlist.Name != null && playlist.Name.StartsWith("Top Rated ("))
			{
				return true;
			}
			return false;
		}

		private static void ImportAnalytics(SqliteConnection connection, SqliteTransaction transaction, PulseFileDatabase fileDb)
		{
			PulseAnalyticsInfo analytics = fileDb.GetAnalytics();
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
		}

		private static void RenameImportedJsonTree(string jsonRootPath)
		{
			string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
			string archived = jsonRootPath + ".imported_" + timestamp;
			try
			{
				Directory.Move(jsonRootPath, archived);
				Log.Info(-1, "Moved imported JSON tree to " + archived);
			}
			catch (Exception ex)
			{
				Log.Warning(-1, "Couldn't rename JSON tree (" + ex.Message + "). Leaving it in place; re-import will be skipped because SQLite is populated.");
			}
		}
	}
}
