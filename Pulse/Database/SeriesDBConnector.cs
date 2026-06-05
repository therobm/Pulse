using Microsoft.Data.Sqlite;

namespace Pulse.Database
{
	/// <summary>
	/// Instance-based SQLite connection factory for the podcast / audiobook
	/// "series" database. Deliberately separate from both the music DB and the
	/// analytics DB: each lives in its own file, evolves its schema
	/// independently, and never shares a connector. WAL +
	/// synchronous=NORMAL on every open so RSS-poll / download writes don't
	/// block reads from the API endpoints once they exist.
	/// </summary>
	public class SeriesDBConnector
	{
		private string m_databaseFilePath;

		public SeriesDBConnector()
		{
			m_databaseFilePath = "pulse_series.db";
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
