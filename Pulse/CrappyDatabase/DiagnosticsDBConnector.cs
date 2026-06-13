using Microsoft.Data.Sqlite;

namespace Pulse.Database
{
	/// <summary>
	/// Instance-based SQLite connection factory for the client-diagnostics
	/// database. Separate file and schema from both the music DB and the
	/// analytics DB. WAL + synchronous=NORMAL on every open so the drain
	/// thread's writes don't block reads from the diagnostics read endpoint.
	/// </summary>
	public class DiagnosticsDBConnector
	{
		private string m_databaseFilePath;

		public DiagnosticsDBConnector()
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
