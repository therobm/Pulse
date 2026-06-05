using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Pulse.Database
{
	/// <summary>
	/// Versioned schema migrations applied on startup, in the style of
	/// Flatline's Database/Migrations.cs. To evolve the schema later: add a
	/// new MigrationStep with the next Version number and the ALTER / CREATE
	/// statements. Never edit a previously-shipped step -- once a deployment
	/// has applied it, the only safe path is a new version.
	/// </summary>
	internal class MigrationStep
	{
		public int Version;
		public string Sql = "";
	}

	public static class Migrations
	{
		public static void RunMigrations()
		{
			SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
			try
			{
				EnsureSchemaVersionsTable(connection);
				int currentVersion = GetCurrentSchemaVersion(connection);

				List<MigrationStep> allMigrations = BuildMigrationList();
				for (int migrationIndex = 0; migrationIndex < allMigrations.Count; migrationIndex++)
				{
					MigrationStep step = allMigrations[migrationIndex];
					if (step.Version > currentVersion)
					{
						ApplyMigration(connection, step);
					}
				}
			}
			finally
			{
				connection.Close();
			}
		}

		private static void EnsureSchemaVersionsTable(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "CREATE TABLE IF NOT EXISTS schema_versions (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);";
			command.ExecuteNonQuery();
		}

		private static int GetCurrentSchemaVersion(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_versions;";
			object result = command.ExecuteScalar();
			return Convert.ToInt32(result);
		}

		private static void ApplyMigration(SqliteConnection connection, MigrationStep step)
		{
			SqliteTransaction transaction = connection.BeginTransaction();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.Transaction = transaction;
				command.CommandText = step.Sql;
				command.ExecuteNonQuery();

				SqliteCommand record = connection.CreateCommand();
				record.Transaction = transaction;
				record.CommandText = "INSERT INTO schema_versions (version, applied_at) VALUES ($v, $t);";
				record.Parameters.AddWithValue("$v", step.Version);
				record.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
				record.ExecuteNonQuery();

				transaction.Commit();
				Log.Info(-1, "Applied schema migration v" + step.Version);
			}
			catch (Exception ex)
			{
				transaction.Rollback();
				Log.Error(-1, "Migration v" + step.Version + " failed: " + ex.Message);
				throw;
			}
		}

		private static List<MigrationStep> BuildMigrationList()
		{
			List<MigrationStep> steps = new List<MigrationStep>();

			MigrationStep v1 = new MigrationStep();
			v1.Version = 1;
			v1.Sql = @"
				CREATE TABLE artists (
					id TEXT PRIMARY KEY,
					name TEXT NOT NULL
				);

				CREATE TABLE albums (
					id TEXT PRIMARY KEY,
					name TEXT NOT NULL,
					artist_name TEXT NOT NULL,
					artist_id TEXT NOT NULL,
					genre TEXT NOT NULL,
					cover_art_id TEXT NOT NULL,
					year INTEGER NOT NULL
				);
				CREATE INDEX idx_albums_artist_id ON albums(artist_id);

				CREATE TABLE tracks (
					id TEXT PRIMARY KEY,
					title TEXT NOT NULL,
					artist TEXT NOT NULL,
					artist_id TEXT NOT NULL,
					album TEXT NOT NULL,
					album_id TEXT NOT NULL,
					genre TEXT NOT NULL,
					file_path TEXT NOT NULL,
					cover_art_id TEXT NOT NULL,
					track_number INTEGER NOT NULL,
					disc_number INTEGER NOT NULL,
					year INTEGER NOT NULL,
					duration_seconds INTEGER NOT NULL,
					file_size_bytes INTEGER NOT NULL,
					content_type TEXT NOT NULL,
					suffix TEXT NOT NULL,
					rating INTEGER NOT NULL,
					last_played TEXT NOT NULL,
					play_count INTEGER NOT NULL,
					skip_count INTEGER NOT NULL,
					total_listen_seconds REAL NOT NULL,
					weighted_score REAL NOT NULL
				);
				CREATE INDEX idx_tracks_album_id ON tracks(album_id);
				CREATE INDEX idx_tracks_artist_id ON tracks(artist_id);

				CREATE TABLE track_user_scores (
					track_id TEXT NOT NULL,
					user_name TEXT NOT NULL,
					play_count INTEGER NOT NULL,
					skip_count INTEGER NOT NULL,
					total_listen_seconds REAL NOT NULL,
					weighted_score REAL NOT NULL,
					PRIMARY KEY (track_id, user_name)
				);
				CREATE INDEX idx_track_user_scores_track_id ON track_user_scores(track_id);

				CREATE TABLE starred (
					entity_kind TEXT NOT NULL,
					entity_id TEXT NOT NULL,
					user_name TEXT NOT NULL,
					starred INTEGER NOT NULL,
					PRIMARY KEY (entity_kind, entity_id, user_name)
				);

				CREATE TABLE playlists (
					id TEXT PRIMARY KEY,
					name TEXT NOT NULL,
					comment TEXT NOT NULL,
					duration_seconds INTEGER NOT NULL
				);

				CREATE TABLE playlist_tracks (
					playlist_id TEXT NOT NULL,
					position INTEGER NOT NULL,
					track_id TEXT NOT NULL,
					PRIMARY KEY (playlist_id, position),
					FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE
				);

				CREATE TABLE analytics_recently_played (
					position INTEGER PRIMARY KEY,
					track_id TEXT NOT NULL
				);
			";
			steps.Add(v1);

			// v2: track LastPlayed for artists and playlists so the left-rail
			// "Recent" sort has something to rank by (Flatline #140). Empty
			// string default = never played; treated as oldest by sort.
			MigrationStep v2 = new MigrationStep();
			v2.Version = 2;
			v2.Sql = @"
				ALTER TABLE artists ADD COLUMN last_played TEXT NOT NULL DEFAULT '';
				ALTER TABLE playlists ADD COLUMN last_played TEXT NOT NULL DEFAULT '';
			";
			steps.Add(v2);

			// v3: per-user playlist last-played so the home carousel can rank
			// by what *this* user actually listens to, not aggregate plays
			// across everyone (Flatline #142).
			MigrationStep v3 = new MigrationStep();
			v3.Version = 3;
			v3.Sql = @"
				CREATE TABLE playlist_user_last_played (
					playlist_id TEXT NOT NULL,
					user_name TEXT NOT NULL,
					last_played TEXT NOT NULL,
					PRIMARY KEY (playlist_id, user_name)
				);
			";
			steps.Add(v3);

			// v4: per-user persistent play queue and bookmarks for the Subsonic
			// getPlayQueue / savePlayQueue / getBookmarks endpoints (Flatline
			// #168). Two tables for the queue (state row + ordered track list)
			// keeps the position int separate from the variable-length entries.
			MigrationStep v4 = new MigrationStep();
			v4.Version = 4;
			v4.Sql = @"
				CREATE TABLE playqueue_state (
					user_name TEXT PRIMARY KEY,
					current_track_id TEXT NOT NULL DEFAULT '',
					position_ms INTEGER NOT NULL DEFAULT 0,
					changed TEXT NOT NULL DEFAULT '',
					changed_by TEXT NOT NULL DEFAULT ''
				);

				CREATE TABLE playqueue_entries (
					user_name TEXT NOT NULL,
					position INTEGER NOT NULL,
					track_id TEXT NOT NULL,
					PRIMARY KEY (user_name, position)
				);

				CREATE TABLE bookmarks (
					user_name TEXT NOT NULL,
					track_id TEXT NOT NULL,
					position_ms INTEGER NOT NULL,
					comment TEXT NOT NULL DEFAULT '',
					created TEXT NOT NULL,
					changed TEXT NOT NULL,
					PRIMARY KEY (user_name, track_id)
				);
				CREATE INDEX idx_bookmarks_user_name ON bookmarks(user_name);
			";
			steps.Add(v4);

			// v5: explicit users table for the settings-page user management
			// (Flatline #201). User identity was previously implicit -- a bare
			// user_name string used as a foreign key in every per-user table.
			// Adding a real users table gives us a stable identity to attach
			// display name / created / is_admin metadata to, and a place to
			// CRUD against. Backfill seeds one row per distinct user_name
			// observed across the existing per-user tables so the new endpoint
			// surfaces every user that already has data.
			MigrationStep v5 = new MigrationStep();
			v5.Version = 5;
			v5.Sql = @"
				CREATE TABLE users (
					name TEXT PRIMARY KEY,
					display_name TEXT NOT NULL DEFAULT '',
					created TEXT NOT NULL,
					is_admin INTEGER NOT NULL DEFAULT 0
				);

				INSERT OR IGNORE INTO users (name, display_name, created, is_admin)
				SELECT name, name, datetime('now'), 0 FROM (
					SELECT DISTINCT user_name AS name FROM track_user_scores
					UNION
					SELECT DISTINCT user_name AS name FROM starred
					UNION
					SELECT DISTINCT user_name AS name FROM playlist_user_last_played
					UNION
					SELECT DISTINCT user_name AS name FROM playqueue_state
					UNION
					SELECT DISTINCT user_name AS name FROM playqueue_entries
					UNION
					SELECT DISTINCT user_name AS name FROM bookmarks
				) WHERE name <> '';
			";
			steps.Add(v5);

			// v6: append-only analytics event log. Every playback state change
			// the clients observe (track started / paused / skipped / completed,
			// plus collection-level started for albums / artists / playlists)
			// lands here as one immutable row. Stored both globally (all rows)
			// and per-user (filter user_name). action and media_type are the
			// enum *names* (PulseWire serializes enums as strings), so the log
			// reads cleanly and survives enum value renumbering. Downstream
			// routes -- RecentlyPlayed, TopPlaylists, track scoring, smart
			// playlists -- aggregate over this table.
			MigrationStep v6 = new MigrationStep();
			v6.Version = 6;
			v6.Sql = @"
				CREATE TABLE analytics_events (
					id INTEGER PRIMARY KEY AUTOINCREMENT,
					occurred_at TEXT NOT NULL,
					user_name TEXT NOT NULL DEFAULT '',
					action TEXT NOT NULL,
					media_type TEXT NOT NULL,
					media_id TEXT NOT NULL
				);
				CREATE INDEX idx_analytics_media ON analytics_events(media_id);
				CREATE INDEX idx_analytics_user_time ON analytics_events(user_name, occurred_at);
				CREATE INDEX idx_analytics_action_type ON analytics_events(action, media_type);
			";
			steps.Add(v6);

			// v7: replace the append-only analytics_events log with a per-(user, item)
			// play counter. The log was only ever read to produce a Started-count and
			// last-played per object; an incremental counter gives the same answer in
			// bounded space with O(1) writes and a keyed read. Backfill the counter from
			// the existing Started events, then drop the log.
			MigrationStep v7 = new MigrationStep();
			v7.Version = 7;
			v7.Sql = @"
				CREATE TABLE item_user_analytics (
					user_name TEXT NOT NULL DEFAULT '',
					media_type TEXT NOT NULL,
					media_id TEXT NOT NULL,
					play_count INTEGER NOT NULL DEFAULT 0,
					last_played TEXT NOT NULL DEFAULT '',
					PRIMARY KEY (user_name, media_type, media_id)
				);
				CREATE INDEX idx_item_analytics_type ON item_user_analytics(media_type);

				INSERT INTO item_user_analytics (user_name, media_type, media_id, play_count, last_played)
					SELECT user_name, media_type, media_id, COUNT(*), MAX(occurred_at)
					FROM analytics_events
					WHERE action = 'Started'
					GROUP BY user_name, media_type, media_id;

				DROP TABLE analytics_events;
			";
			steps.Add(v7);

			return steps;
		}
	}
}
