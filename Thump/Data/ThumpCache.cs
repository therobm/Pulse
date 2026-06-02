using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;

namespace Thump.Data
{
	public class ThumpCacheStats
	{
		public long BytesUsed;
		public int EntryCount;
		public long OldestFetchedUnix;
	}

	public class ThumpCache
	{
		private SqliteConnection m_connection;
		private string m_connectionString;
		private long m_sizeLimitBytes;

		private object m_sqlLock = new object();

		public ThumpCache()
		{
			string cacheRoot = FileSystem.CacheDirectory;
			string databasePath = Path.Combine(cacheRoot, "thump.db");
			m_connectionString = "Data Source=" + databasePath;
			Initalize();
		}

		private void Initalize()
		{
			try
			{
				m_connection = new SqliteConnection(m_connectionString);
				m_connection.Open();
				ApplyPragmas();
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				System.Diagnostics.Debugger.Break();
				return;
			}
		}

		private void ApplyPragmas()
		{
			using (SqliteCommand cmd = m_connection.CreateCommand())
			{
				cmd.CommandText = "PRAGMA journal_mode=WAL;PRAGMA foreign_keys=ON;";
				cmd.ExecuteNonQuery();
			}
			using (SqliteCommand cmd = m_connection.CreateCommand())
			{
				cmd.CommandText = "CREATE TABLE IF NOT EXISTS http_cache (url TEXT PRIMARY KEY, data BLOB NOT NULL, fetched_at INTEGER NOT NULL);";
				cmd.ExecuteNonQuery();
			}
		}

		public void ExecuteSync(Action work)
		{
			lock (m_sqlLock)
			{
				if (work != null)
				{
					try
					{
						work();
					}
					catch (Exception ex)
					{
						Log.Exception(ex);
					}
				}
			}
		}

		public void CacheQueryResults(string url, byte[] data)
		{
			if (string.IsNullOrEmpty(url) || data == null)
			{
				return;
			}
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			lock (m_sqlLock)
			{
				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "INSERT OR REPLACE INTO http_cache (url, data, fetched_at) VALUES ($u, $d, $f)";
					cmd.Parameters.AddWithValue("$u", url);
					cmd.Parameters.AddWithValue("$d", data);
					cmd.Parameters.AddWithValue("$f", now);
					cmd.ExecuteNonQuery();
				}
			}
			EnforceCachingLimits();
		}

		public void CacheQueryResults(string url, string data)
		{
			if (string.IsNullOrEmpty(url) || data == null)
			{
				return;
			}
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
			CacheQueryResults(url, bytes);
		}

		public bool GetCachedResults(string url, out byte[] data)
		{
			data = null;
			if (string.IsNullOrEmpty(url))
			{
				return false;
			}
			lock (m_sqlLock)
			{
				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "SELECT data FROM http_cache WHERE url = $u";
					cmd.Parameters.AddWithValue("$u", url);
					object result = cmd.ExecuteScalar();
					if (result == null)
					{
						return false;
					}
					if (result == DBNull.Value)
					{
						return false;
					}
					data = (byte[])result;
					return true;
				}
			}
		}

		public bool GetCachedResults(string url, out string data)
		{
			data = null;
			byte[] bytes;
			if (!GetCachedResults(url, out bytes))
			{
				return false;
			}
			data = System.Text.Encoding.UTF8.GetString(bytes);
			return true;
		}

		public byte[] GetTrackAudioFromCache(string url)
		{
			byte[] data;
			if (!GetCachedResults(url, out data))
			{
				return null;
			}
			return data;
		}

		public ThumpCacheStats GetCacheStats()
		{
			ThumpCacheStats stats = new ThumpCacheStats();
			lock (m_sqlLock)
			{
				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "SELECT COALESCE(SUM(LENGTH(data)), 0), COUNT(*), COALESCE(MIN(fetched_at), 0) FROM http_cache";
					using (SqliteDataReader reader = cmd.ExecuteReader())
					{
						if (reader.Read())
						{
							stats.BytesUsed = reader.GetInt64(0);
							stats.EntryCount = reader.GetInt32(1);
							stats.OldestFetchedUnix = reader.GetInt64(2);
						}
					}
				}
			}
			return stats;
		}

		public void ClearCache()
		{
			lock (m_sqlLock)
			{
				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "DELETE FROM http_cache";
					cmd.ExecuteNonQuery();
				}
			}
		}

		public void SetSizeLimitBytes(long limitBytes)
		{
			m_sizeLimitBytes = limitBytes;
		}

		//Hook for the LRU/age-based eviction pass. Called on every write so
		//that once a real policy lands here the cache self-trims. Intentionally
		//empty for now — eviction policy lands in a follow-up.
		private void EnforceCachingLimits()
		{
			if (m_sizeLimitBytes <= 0)
			{
				return;
			}
			//TODO eviction: walk http_cache ORDER BY fetched_at ASC, drop rows until
			//SUM(LENGTH(data)) <= m_sizeLimitBytes.
		}
	}
}
