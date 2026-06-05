using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Pulse.Data
{
	/// <summary>
	/// One step in the analyticss-database schema history. The analyticss db
	/// keeps its own schema_versions table inside its own file -- it does NOT
	/// share the music db's migration ledger. Adding a new step: pick the next
	/// version number and append a new MigrationStep entry; never edit a
	/// previously-shipped step.
	/// </summary>
	internal class analyticssMigrationStep
	{
		public int Version;
		public string Sql;

		public analyticssMigrationStep()
		{
			Version = 0;
			Sql = "";
		}
	}

	/// <summary>
	/// Versioned schema migrations for the analyticss database. Instance-based
	/// so each PulseService owns its own runner against its own factory.
	/// </summary>
	public class AnalyticsDBMigrations
	{
		private AnalyticsDBConnector m_connector;

		public AnalyticsDBMigrations(AnalyticsDBConnector connector)
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

				List<analyticssMigrationStep> allMigrations = BuildMigrationList();
				int migrationCount = allMigrations.Count;
				for (int migrationIndex = 0; migrationIndex < migrationCount; migrationIndex++)
				{
					analyticssMigrationStep step = allMigrations[migrationIndex];
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

		private void ApplyMigration(SqliteConnection connection, analyticssMigrationStep step)
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
				Log.Info(-1, "Applied analyticss schema migration v" + step.Version);
			}
			catch (Exception ex)
			{
				transaction.Rollback();
				Log.Error(-1, "analyticss migration v" + step.Version + " failed: " + ex.Message);
				throw;
			}
		}

		private List<analyticssMigrationStep> BuildMigrationList()
		{
			List<analyticssMigrationStep> steps = new List<analyticssMigrationStep>();

			analyticssMigrationStep v1 = new analyticssMigrationStep();
			v1.Version = 1;
			v1.Sql = @"
				CREATE TABLE sessions (
					session_id TEXT PRIMARY KEY,
					device_id TEXT NOT NULL,
					user TEXT,
					app_version TEXT,
					platform TEXT,
					started_at TEXT NOT NULL
				);

				CREATE TABLE log_events (
					id INTEGER PRIMARY KEY AUTOINCREMENT,
					session_id TEXT NOT NULL,
					timestamp TEXT NOT NULL,
					action TEXT NOT NULL,
					location TEXT NOT NULL,
					result TEXT NOT NULL,
					detail TEXT,
					received_at TEXT NOT NULL,
					FOREIGN KEY (session_id) REFERENCES sessions(session_id)
				);

				CREATE INDEX idx_events_session_time ON log_events (session_id, timestamp);
				CREATE INDEX idx_events_action ON log_events (action);
				CREATE INDEX idx_events_result ON log_events (result);
			";
			steps.Add(v1);

			return steps;
		}
	}
}
