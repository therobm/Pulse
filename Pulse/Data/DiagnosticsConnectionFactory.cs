using Microsoft.Data.Sqlite;

namespace Pulse.Data
{
	/// <summary>
	/// Instance-based SQLite connection factory for the diagnostic / telemetry
	/// log database. Deliberately separate from the music DB factory: the two
	/// databases live in different files, evolve their schemas independently,
	/// and must not share the singleton path that the music side uses. WAL +
	/// synchronous=NORMAL on every open so the drain thread's writes don't
	/// block reads from the diagnostics read endpoint.
	/// </summary>
	public class DiagnosticsConnectionFactory
	{
		private string m_databaseFilePath;

		public DiagnosticsConnectionFactory()
		{
			m_databaseFilePath = "pulse_diagnostics.db";
		}

		public void SetDatabaseFilePath(string filePath)
		{
			m_databaseFilePath = filePath;
		}

		public string GetDatabaseFilePath()
		{
			return m_databaseFilePath;
		}

		public string GetConnectionString()
		{
			SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder();
			builder.DataSource = m_databaseFilePath;
			builder.ForeignKeys = true;
			return builder.ToString();
		}

		public SqliteConnection OpenConnection()
		{
			SqliteConnection connection = new SqliteConnection(GetConnectionString());
			connection.Open();

			SqliteCommand pragma = connection.CreateCommand();
			pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
			pragma.ExecuteNonQuery();

			return connection;
		}
	}
}
