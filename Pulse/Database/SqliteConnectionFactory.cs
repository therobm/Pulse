using Microsoft.Data.Sqlite;

namespace Pulse.Database
{
	/// <summary>
	/// One-stop place to open SQLite connections against the active Pulse DB
	/// file. Path is set once at startup by PulseService / MusicManager based
	/// on PulseConfig.DatabaseEnvironment. WAL mode is turned on after open so
	/// readers don't block the background scan-thread's writes.
	/// </summary>
	public static class SqliteConnectionFactory
	{
		private static string s_databaseFilePath = "pulse.db";

		public static void SetDatabaseFilePath(string filePath)
		{
			s_databaseFilePath = filePath;
		}

		public static string GetDatabaseFilePath()
		{
			return s_databaseFilePath;
		}

		public static string GetConnectionString()
		{
			SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder();
			builder.DataSource = s_databaseFilePath;
			builder.ForeignKeys = true;
			return builder.ToString();
		}

		public static SqliteConnection OpenConnection()
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
