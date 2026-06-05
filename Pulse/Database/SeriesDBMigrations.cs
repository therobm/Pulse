using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Pulse.Database
{
	/// <summary>
	/// One step in the series-database schema history. The series db keeps its
	/// own schema_versions table inside its own file -- it does NOT share the
	/// music db's or the analytics db's migration ledger. Adding a new step:
	/// pick the next version number and append a new MigrationStep entry;
	/// never edit a previously-shipped step.
	/// </summary>
	internal class SeriesMigrationStep
	{
		public int Version;
		public string Sql;

		public SeriesMigrationStep()
		{
			Version = 0;
			Sql = "";
		}
	}

	/// <summary>
	/// Versioned schema migrations for the series database. Instance-based so
	/// each PulseService owns its own runner against its own factory.
	/// </summary>
	public class SeriesDBMigrations
	{
		private SeriesDBConnector m_connector;

		public SeriesDBMigrations(SeriesDBConnector connector)
		{
			m_connector = connector;
		}

		public void RunMigrations()
		{
			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				EnsureSchemaVersionsTable(connection);
				int currentVersion = GetCurrentSchemaVersion(connection);

				List<SeriesMigrationStep> allMigrations = BuildMigrationList();
				int migrationCount = allMigrations.Count;
				for (int migrationIndex = 0; migrationIndex < migrationCount; migrationIndex++)
				{
					SeriesMigrationStep step = allMigrations[migrationIndex];
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

		private void EnsureSchemaVersionsTable(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "CREATE TABLE IF NOT EXISTS schema_versions (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);";
			command.ExecuteNonQuery();
		}

		private int GetCurrentSchemaVersion(SqliteConnection connection)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_versions;";
			object result = command.ExecuteScalar();
			return Convert.ToInt32(result);
		}

		private void ApplyMigration(SqliteConnection connection, SeriesMigrationStep step)
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
				Log.Info(-1, "Applied series schema migration v" + step.Version);
			}
			catch (Exception ex)
			{
				transaction.Rollback();
				Log.Error(-1, "series migration v" + step.Version + " failed: " + ex.Message);
				throw;
			}
		}

		private List<SeriesMigrationStep> BuildMigrationList()
		{
			List<SeriesMigrationStep> steps = new List<SeriesMigrationStep>();

			SeriesMigrationStep v1 = new SeriesMigrationStep();
			v1.Version = 1;
			v1.Sql = @"
				CREATE TABLE series (
					id TEXT PRIMARY KEY,
					series_type TEXT NOT NULL,
					title TEXT NOT NULL,
					author TEXT,
					description TEXT,
					artwork_path TEXT,
					date_added TEXT NOT NULL,
					narrator TEXT,
					collection TEXT,
					collection_index INTEGER NOT NULL DEFAULT 0
				);

				CREATE TABLE podcast_feeds (
					series_id TEXT PRIMARY KEY REFERENCES series(id) ON DELETE CASCADE,
					feed_url TEXT NOT NULL,
					last_polled TEXT,
					poll_interval_minutes INTEGER NOT NULL DEFAULT 60,
					retention_policy TEXT NOT NULL,
					retention_value INTEGER NOT NULL DEFAULT 0,
					auto_download INTEGER NOT NULL DEFAULT 0
				);

				CREATE TABLE series_items (
					id TEXT PRIMARY KEY,
					series_id TEXT NOT NULL REFERENCES series(id) ON DELETE CASCADE,
					guid TEXT,
					title TEXT NOT NULL,
					description TEXT,
					duration_seconds INTEGER NOT NULL DEFAULT 0,
					order_index INTEGER NOT NULL DEFAULT 0,
					published_date TEXT,
					media_source_url TEXT,
					local_path TEXT,
					file_size_bytes INTEGER NOT NULL DEFAULT 0,
					download_state TEXT NOT NULL
				);

				CREATE INDEX idx_series_items_series_id ON series_items(series_id);
				CREATE INDEX idx_series_items_downloaded ON series_items(series_id, local_path);

				CREATE TABLE item_progress (
					item_id TEXT NOT NULL REFERENCES series_items(id) ON DELETE CASCADE,
					user_name TEXT NOT NULL,
					position_seconds INTEGER NOT NULL DEFAULT 0,
					completed INTEGER NOT NULL DEFAULT 0,
					last_played TEXT,
					PRIMARY KEY (item_id, user_name)
				);

				CREATE TABLE user_series (
					series_id TEXT NOT NULL REFERENCES series(id) ON DELETE CASCADE,
					user_name TEXT NOT NULL,
					subscribed INTEGER NOT NULL DEFAULT 0,
					last_item_id TEXT,
					last_played TEXT,
					date_added TEXT NOT NULL,
					PRIMARY KEY (series_id, user_name)
				);

				CREATE INDEX idx_user_series_user ON user_series(user_name);
			";
			steps.Add(v1);

			return steps;
		}
	}
}
