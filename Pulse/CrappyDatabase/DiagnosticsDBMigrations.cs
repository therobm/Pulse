using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Pulse.Database
{
	/// <summary>
	/// One step in the diagnostics-database schema history. The diagnostics DB
	/// keeps its own schema_versions table inside its own file. Adding a step:
	/// pick the next version number and append a new entry; never edit a
	/// previously-shipped step.
	/// </summary>
	internal class DiagnosticsMigrationStep
	{
		public int Version;
		public string Sql;

		public DiagnosticsMigrationStep()
		{
			Version = 0;
			Sql = "";
		}
	}

	/// <summary>
	/// Versioned schema migrations for the diagnostics database. Instance-based
	/// so each PulseService owns its own runner against its own factory.
	/// </summary>
	public class DiagnosticsDBMigrations
	{
		private DiagnosticsDBConnector m_connector;

		public DiagnosticsDBMigrations(DiagnosticsDBConnector connector)
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

				List<DiagnosticsMigrationStep> allMigrations = BuildMigrationList();
				int migrationCount = allMigrations.Count;
				for (int migrationIndex = 0; migrationIndex < migrationCount; migrationIndex++)
				{
					DiagnosticsMigrationStep step = allMigrations[migrationIndex];
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

		private void ApplyMigration(SqliteConnection connection, DiagnosticsMigrationStep step)
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
				Log.Info("Applied diagnostics schema migration v" + step.Version);
			}
			catch (Exception ex)
			{
				transaction.Rollback();
				Log.Error("diagnostics migration v" + step.Version + " failed: " + ex.Message);
				throw;
			}
		}

		private List<DiagnosticsMigrationStep> BuildMigrationList()
		{
			List<DiagnosticsMigrationStep> steps = new List<DiagnosticsMigrationStep>();

			DiagnosticsMigrationStep v1 = new DiagnosticsMigrationStep();
			v1.Version = 1;
			v1.Sql = @"
				CREATE TABLE diagnostics (
					id INTEGER PRIMARY KEY AUTOINCREMENT,
					device_id TEXT,
					session_id TEXT,
					app_version TEXT,
					build_number INTEGER,
					user TEXT,
					platform TEXT,
					os_version TEXT,
					device_model TEXT,
					network_type TEXT,
					caller TEXT,
					member_name TEXT,
					error_message TEXT,
					detail TEXT,
					timestamp TEXT,
					received_at TEXT NOT NULL
				);

				CREATE INDEX idx_diagnostics_received ON diagnostics (received_at);
				CREATE INDEX idx_diagnostics_build ON diagnostics (build_number);
				CREATE INDEX idx_diagnostics_location ON diagnostics (caller, member_name);
				CREATE INDEX idx_diagnostics_device ON diagnostics (device_id);
				CREATE INDEX idx_diagnostics_network ON diagnostics (network_type);
			";
			steps.Add(v1);

			return steps;
		}
	}
}
