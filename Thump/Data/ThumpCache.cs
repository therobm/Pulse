using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
				// busy_timeout: the UI and the Android Auto playback service each hold
				// their own connection to this DB, so a contended writer must wait and
				// retry rather than fail fast with SQLITE_BUSY (which ExecuteSync would
				// swallow, silently dropping the cache write).
				cmd.CommandText = "PRAGMA journal_mode=WAL;PRAGMA foreign_keys=ON;PRAGMA busy_timeout=5000;";
				cmd.ExecuteNonQuery();
			}
			using (SqliteCommand cmd = m_connection.CreateCommand())
			{
				cmd.CommandText = "CREATE TABLE IF NOT EXISTS http_cache (url TEXT PRIMARY KEY, data BLOB NOT NULL, fetched_at INTEGER NOT NULL, is_binary INTEGER NOT NULL DEFAULT 0);";
				cmd.ExecuteNonQuery();
			}
			EnsureBinaryColumn();
		}

		//Older caches predate the is_binary column. Add it in place so existing
		//databases pick up binary-aware eviction without being wiped.
		private void EnsureBinaryColumn()
		{
			bool hasColumn = false;
			using (SqliteCommand cmd = m_connection.CreateCommand())
			{
				cmd.CommandText = "PRAGMA table_info(http_cache)";
				using (SqliteDataReader reader = cmd.ExecuteReader())
				{
					while (reader.Read())
					{
						string columnName = reader.GetString(1);
						if (columnName == "is_binary")
						{
							hasColumn = true;
							break;
						}
					}
				}
			}
			if (hasColumn)
			{
				return;
			}
			using (SqliteCommand cmd = m_connection.CreateCommand())
			{
				cmd.CommandText = "ALTER TABLE http_cache ADD COLUMN is_binary INTEGER NOT NULL DEFAULT 0;";
				cmd.ExecuteNonQuery();
			}
		}
		public void ExecuteAsync(Action work)
		{
			if (work == null)
			{
				return;
			}
			Task.Run(() =>
			{
				ExecuteSync(work);
			});
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


		public void CacheQueryResults(string url, string data)
		{
			if (string.IsNullOrWhiteSpace(url) || data == null || data.Length == 0)
			{
				return;
			}
			Log.Info("Caching: URL: " + url + " DATA: " + data);
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
			CacheBytes(url, bytes, false);
		}

		public void CacheQueryResults(string url, byte[] data)
		{
			if (string.IsNullOrEmpty(url) || data == null || data.Length == 0)
			{
				return;
			}
			CacheBytes(url, data, true);
		}

		//isBinary marks expensive-to-refetch payloads (audio, cover art) so the
		//eviction pass can drop cheap JSON metadata first. The string overload
		//passes false, the byte[] overload passes true.
		private void CacheBytes(string url, byte[] data, bool isBinary)
		{
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			long binaryFlag = 0;
			if (isBinary)
			{
				binaryFlag = 1;
			}

			ExecuteAsync(()=>
			{
				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "INSERT OR REPLACE INTO http_cache (url, data, fetched_at, is_binary) VALUES ($u, $d, $f, $b)";
					cmd.Parameters.AddWithValue("$u", url);
					cmd.Parameters.AddWithValue("$d", data);
					cmd.Parameters.AddWithValue("$f", now);
					cmd.Parameters.AddWithValue("$b", binaryFlag);
					cmd.ExecuteNonQuery();
				}
			});
			EnforceCachingLimits();
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

		public bool GetCachedResults(string url, out byte[] data)
		{
			byte[] retData = null;
			data = null;
			if (string.IsNullOrEmpty(url))
			{
				return false;
			}
			
			ExecuteSync(()=>
			{
				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "SELECT data FROM http_cache WHERE url = $u";
					cmd.Parameters.AddWithValue("$u", url);
					object result = cmd.ExecuteScalar();
					if (result != null && result != DBNull.Value)
					{ 
						retData = result as byte[];
						if (retData == null || retData.Length <= 0)
						{
							Log.Error("Invalid data cached for " + url);
							using (SqliteCommand deleteCmd = m_connection.CreateCommand())
							{
								deleteCmd.CommandText = "DELETE FROM http_cache WHERE url = $u";
								deleteCmd.Parameters.AddWithValue("$u", url);
								deleteCmd.ExecuteNonQuery();
							}
						}
					}
				}
			});

			data = retData;
			return data != null && data.Length > 0;
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

		/// <summary>True when an entry for the given URL exists in the cache. Existence-only — does not read the blob back, so it is safe to call on large audio entries.</summary>
		public bool HasCachedResults(string url)
		{
			if (string.IsNullOrEmpty(url))
			{
				return false;
			}
			bool exists = false;
			ExecuteSync(() =>
			{
				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "SELECT 1 FROM http_cache WHERE url = $u LIMIT 1";
					cmd.Parameters.AddWithValue("$u", url);
					object result = cmd.ExecuteScalar();
					if (result != null && result != DBNull.Value)
					{
						exists = true;
					}
				}
			});
			return exists;
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

		//Age-based eviction with a binary bias. Called on every write so the
		//cache self-trims. Cheap-to-refetch JSON metadata (is_binary = 0) is
		//dropped before expensive audio/cover-art blobs, and within each tier
		//the oldest entries go first.
		private void EnforceCachingLimits()
		{
			if (m_sizeLimitBytes <= 0)
			{
				return;
			}

			lock (m_sqlLock)
			{
				long totalBytes = 0;
				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "SELECT COALESCE(SUM(LENGTH(data)), 0) FROM http_cache";
					totalBytes = Convert.ToInt64(cmd.ExecuteScalar());
				}

				if (totalBytes <= m_sizeLimitBytes)
				{
					return;
				}

				long bytesToFree = totalBytes - m_sizeLimitBytes;
				long freedBytes = 0;
				List<string> urlsToEvict = new List<string>();

				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					cmd.CommandText = "SELECT url, LENGTH(data) FROM http_cache ORDER BY is_binary ASC, fetched_at ASC";
					using (SqliteDataReader reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							string url = reader.GetString(0);
							long entryBytes = reader.GetInt64(1);
							urlsToEvict.Add(url);
							freedBytes += entryBytes;
							if (freedBytes >= bytesToFree)
							{
								break;
							}
						}
					}
				}

				if (urlsToEvict.Count == 0)
				{
					return;
				}

				using (SqliteCommand cmd = m_connection.CreateCommand())
				{
					StringBuilder sb = new StringBuilder();
					sb.Append("DELETE FROM http_cache WHERE url IN (");
					for (int i = 0; i < urlsToEvict.Count; i++)
					{
						string paramName = "$u" + i;
						if (i > 0)
						{
							sb.Append(",");
						}
						sb.Append(paramName);
						cmd.Parameters.AddWithValue(paramName, urlsToEvict[i]);
					}
					sb.Append(")");
					cmd.CommandText = sb.ToString();
					cmd.ExecuteNonQuery();
				}
			}
		}
	}
}
