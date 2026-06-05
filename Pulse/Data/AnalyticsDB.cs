using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using PulseAPI.CSharp;

namespace Pulse.Data
{
	/// <summary>
	/// High-level grouping an action rolls up to. Derived from the action on
	/// ingest (see AnalyticsDB.CategoryOf) and stored on each row so reporting
	/// can group at the category level without re-deriving. Uncategorized is the
	/// fallback for an action the map doesn't yet cover.
	/// </summary>
	public enum eCategory
	{
		Uncategorized,
		App,
		Navigation,
		Search,
		Playback,
		Library,
		Network
	}

	/// <summary>
	/// One row in the analytics sessions table, in the shape the /analytics read
	/// endpoint returns it. Plain public fields; serialized through PulseWire
	/// which emits field names verbatim.
	/// </summary>
	public class AnalyticsSessionRow
	{
		public string SessionId;
		public string DeviceId;
		public string User;
		public string AppVersion;
		public string Platform;
		public string StartedAt;
	}

	/// <summary>
	/// One row in the analytics events table, in the shape the /analytics read
	/// endpoint returns it. DurationMs is -1 when the stored value was NULL
	/// (instantaneous action); ObjectType/ObjectId are empty when the event had
	/// no object.
	/// </summary>
	public class AnalyticsEventRow
	{
		public long Id;
		public string SessionId;
		public string Timestamp;
		public string ReceivedAt;
		public string Category;
		public string Action;
		public string Result;
		public string ObjectType;
		public string ObjectId;
		public long DurationMs;
		public string Detail;
	}

	/// <summary>
	/// Carrier the route handler hands to the drain queue: the parsed batch plus
	/// the server-stamped received_at string. The received_at lives only on this
	/// carrier -- it is NOT part of the public PulseAnalyticsBatch wire type, so
	/// the client never sees or sends it.
	/// </summary>
	internal class AnalyticsBatchItem
	{
		public PulseAnalyticsBatch Batch;
		public string ReceivedAt;
	}

	/// <summary>
	/// Intake + drain ("firehose") for the analytics pipeline. The route handler
	/// calls Enqueue from the request thread; a single background drain thread
	/// pulls batches off the queue, opens one transaction per batch, upserts the
	/// session row, and inserts every event in the batch. The drain thread also
	/// runs a 30-day retention prune at startup and on a 6-hour cadence after
	/// that. The request thread never touches the database.
	/// </summary>
	public class AnalyticsDB
	{
		private AnalyticsDBConnector m_connector;
		private BlockingCollection<AnalyticsBatchItem> m_queue;
		private Thread m_drainThread;
		private TimeSpan m_takeTimeout;
		private TimeSpan m_pruneInterval;
		private int m_retentionDays;
		private DateTime m_lastPruneUtc;

		public AnalyticsDB(PulseConfig config)
		{
			string environmentName = config.DatabaseEnvironment;
			if (string.IsNullOrWhiteSpace(environmentName))
			{
				environmentName = "Production";
			}
#if DEBUG
			if (!string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase))
			{
				Log.Warning(-1, "Debugger attached: forcing Staging environment for analytics DB (config said '" + environmentName + "').");
			}
			environmentName = "Staging";
#endif

			string pulseDataRoot = Path.Combine(config.MusicPath, "PulseData");
			if (!Directory.Exists(pulseDataRoot))
			{
				Directory.CreateDirectory(pulseDataRoot);
			}

			string analyticsFileName = "pulse_analytics_" + environmentName.ToLowerInvariant() + ".db";
			string analyticsPath = Path.Combine(pulseDataRoot, analyticsFileName);

			m_connector = new AnalyticsDBConnector();
			m_connector.SetDatabaseFilePath(analyticsPath);

			AnalyticsDBMigrations analyticsMigrations = new AnalyticsDBMigrations(m_connector);
			analyticsMigrations.RunMigrations();
			Log.Info(-1, "Pulse analytics DB: env=" + environmentName + " path=" + analyticsPath);

			m_queue = new BlockingCollection<AnalyticsBatchItem>();
			m_takeTimeout = TimeSpan.FromMinutes(1);
			m_pruneInterval = TimeSpan.FromHours(6);
			m_retentionDays = 30;
			m_lastPruneUtc = DateTime.MinValue;

			m_drainThread = new Thread(DrainLoop);
			m_drainThread.IsBackground = true;
			m_drainThread.Name = "PulseAnalyticsDrain";
			m_drainThread.Start();
		}

		/// <summary>
		/// The single source of truth for the action-to-category taxonomy. Run at
		/// ingest to fill the category column. A new action with no case here
		/// falls to Uncategorized rather than being dropped -- the raw action is
		/// still stored, so such rows can be re-categorised later once the
		/// mapping is added.
		/// </summary>
		private eCategory CategoryOf(eAction action)
		{
			switch (action)
			{
				case eAction.Launch:
				case eAction.Quit:
				case eAction.Login:
				case eAction.SettingsChange:
					return eCategory.App;
				case eAction.Browse:
				case eAction.OpenNowPlaying:
				case eAction.Tab:
					return eCategory.Navigation;
				case eAction.Search:
					return eCategory.Search;
				case eAction.Play:
				case eAction.Pause:
				case eAction.Resume:
				case eAction.Next:
				case eAction.Previous:
				case eAction.Seek:
				case eAction.QueueAdd:
				case eAction.ModeChange:
				case eAction.TrackLoad:
				case eAction.TrackStream:
					return eCategory.Playback;
				case eAction.PlaylistCreate:
				case eAction.PlaylistEdit:
				case eAction.FavoriteToggle:
					return eCategory.Library;
				case eAction.Connectivity:
				case eAction.Scrobble:
					return eCategory.Network;
				default:
					return eCategory.Uncategorized;
			}
		}

		/// <summary>
		/// Called from the request thread. Wraps the parsed batch with the
		/// server-stamped received_at and adds it to the drain queue. Does not
		/// touch SQLite.
		/// </summary>
		public void Enqueue(PulseAnalyticsBatch batch, string receivedAt)
		{
			if (batch == null)
			{
				return;
			}
			AnalyticsBatchItem item = new AnalyticsBatchItem();
			item.Batch = batch;
			item.ReceivedAt = receivedAt;
			m_queue.Add(item);
		}

		private void DrainLoop()
		{
			// One prune at startup so the file doesn't carry yesterday's noise
			// forward across a restart.
			TryPrune();

			while (true)
			{
				try
				{
					AnalyticsBatchItem item;
					bool gotItem = m_queue.TryTake(out item, m_takeTimeout);
					if (gotItem)
					{
						if (item != null)
						{
							WriteBatch(item);
						}
					}

					DateTime nowUtc = DateTime.UtcNow;
					if (nowUtc - m_lastPruneUtc >= m_pruneInterval)
					{
						TryPrune();
					}
				}
				catch (Exception ex)
				{
					Log.Error(-1, "AnalyticsDB drain failed: " + ex.Message);
				}
			}
		}

		private void WriteBatch(AnalyticsBatchItem item)
		{
			PulseAnalyticsBatch batch = item.Batch;
			if (batch == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(batch.SessionId))
			{
				return;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteTransaction transaction = connection.BeginTransaction();
				try
				{
					UpsertSession(connection, transaction, batch, item.ReceivedAt);

					if (batch.Events != null)
					{
						int eventCount = batch.Events.Count;
						for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
						{
							PulseAnalyticsEvent analyticsEvent = batch.Events[eventIndex];
							if (analyticsEvent == null)
							{
								continue;
							}
							InsertEvent(connection, transaction, batch.SessionId, analyticsEvent, item.ReceivedAt);
						}
					}

					transaction.Commit();
				}
				catch (Exception ex)
				{
					transaction.Rollback();
					Log.Error(-1, "AnalyticsDB.WriteBatch failed: " + ex.Message);
				}
			}
			finally
			{
				connection.Close();
			}
		}

		private void UpsertSession(SqliteConnection connection, SqliteTransaction transaction, PulseAnalyticsBatch batch, string receivedAt)
		{
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "INSERT OR IGNORE INTO sessions (session_id, device_id, user, app_version, platform, started_at) VALUES ($sessionId, $deviceId, $user, $appVersion, $platform, $startedAt);";

			string deviceId = "";
			if (batch.DeviceId != null)
			{
				deviceId = batch.DeviceId;
			}
			string user = "";
			if (batch.User != null)
			{
				user = batch.User;
			}
			string appVersion = "";
			if (batch.AppVersion != null)
			{
				appVersion = batch.AppVersion;
			}
			string platform = "";
			if (batch.Platform != null)
			{
				platform = batch.Platform;
			}

			command.Parameters.AddWithValue("$sessionId", batch.SessionId);
			command.Parameters.AddWithValue("$deviceId", deviceId);
			command.Parameters.AddWithValue("$user", user);
			command.Parameters.AddWithValue("$appVersion", appVersion);
			command.Parameters.AddWithValue("$platform", platform);
			command.Parameters.AddWithValue("$startedAt", receivedAt);
			command.ExecuteNonQuery();
		}

		private void InsertEvent(SqliteConnection connection, SqliteTransaction transaction, string sessionId, PulseAnalyticsEvent analyticsEvent, string receivedAt)
		{
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "INSERT INTO events (session_id, timestamp, received_at, category, action, result, object_type, object_id, duration_ms, detail) VALUES ($sessionId, $timestamp, $receivedAt, $category, $action, $result, $objectType, $objectId, $durationMs, $detail);";

			string timestamp = "";
			if (analyticsEvent.Timestamp != null)
			{
				timestamp = analyticsEvent.Timestamp;
			}
			string objectType = "";
			if (analyticsEvent.ObjectType != null)
			{
				objectType = analyticsEvent.ObjectType;
			}
			string objectId = "";
			if (analyticsEvent.ObjectId != null)
			{
				objectId = analyticsEvent.ObjectId;
			}
			string detail = "";
			if (analyticsEvent.Detail != null)
			{
				detail = analyticsEvent.Detail;
			}

			eCategory category = CategoryOf(analyticsEvent.Action);

			command.Parameters.AddWithValue("$sessionId", sessionId);
			command.Parameters.AddWithValue("$timestamp", timestamp);
			command.Parameters.AddWithValue("$receivedAt", receivedAt);
			command.Parameters.AddWithValue("$category", category.ToString());
			command.Parameters.AddWithValue("$action", analyticsEvent.Action.ToString());
			command.Parameters.AddWithValue("$result", analyticsEvent.Result.ToString());

			// No object -> store NULL for both object columns so they don't
			// pollute GROUP BY object_type.
			if (string.IsNullOrEmpty(objectId))
			{
				command.Parameters.AddWithValue("$objectType", DBNull.Value);
				command.Parameters.AddWithValue("$objectId", DBNull.Value);
			}
			else
			{
				command.Parameters.AddWithValue("$objectType", objectType);
				command.Parameters.AddWithValue("$objectId", objectId);
			}

			// Instantaneous actions arrive as -1 -> store NULL duration.
			if (analyticsEvent.DurationMs < 0)
			{
				command.Parameters.AddWithValue("$durationMs", DBNull.Value);
			}
			else
			{
				command.Parameters.AddWithValue("$durationMs", analyticsEvent.DurationMs);
			}

			if (string.IsNullOrEmpty(detail))
			{
				command.Parameters.AddWithValue("$detail", DBNull.Value);
			}
			else
			{
				command.Parameters.AddWithValue("$detail", detail);
			}

			command.ExecuteNonQuery();
		}

		private void TryPrune()
		{
			try
			{
				PruneOldEvents();
			}
			catch (Exception ex)
			{
				Log.Error(-1, "AnalyticsDB prune failed: " + ex.Message);
			}
			m_lastPruneUtc = DateTime.UtcNow;
		}

		private void PruneOldEvents()
		{
			string cutoff = DateTime.UtcNow.AddDays(-m_retentionDays).ToString("o");

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "DELETE FROM events WHERE received_at < $cutoff;";
				command.Parameters.AddWithValue("$cutoff", cutoff);
				int deleted = command.ExecuteNonQuery();
				if (deleted > 0)
				{
					Log.Info(-1, "Analytics retention prune removed " + deleted + " event(s) older than " + cutoff);
				}
			}
			finally
			{
				connection.Close();
			}
		}

		/// <summary>
		/// Read every event for a given session, ordered by client timestamp.
		/// Empty category / action / result filters mean "no filter on that
		/// column".
		/// </summary>
		public List<AnalyticsEventRow> GetEventsForSession(string sessionId, string categoryFilter, string actionFilter, string resultFilter)
		{
			List<AnalyticsEventRow> rows = new List<AnalyticsEventRow>();
			if (string.IsNullOrEmpty(sessionId))
			{
				return rows;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				bool hasCategory = !string.IsNullOrEmpty(categoryFilter);
				bool hasAction = !string.IsNullOrEmpty(actionFilter);
				bool hasResult = !string.IsNullOrEmpty(resultFilter);

				string sql = "SELECT id, session_id, timestamp, received_at, category, action, result, object_type, object_id, duration_ms, detail FROM events WHERE session_id = $sessionId";
				if (hasCategory)
				{
					sql = sql + " AND category = $category";
				}
				if (hasAction)
				{
					sql = sql + " AND action = $action";
				}
				if (hasResult)
				{
					sql = sql + " AND result = $result";
				}
				sql = sql + " ORDER BY timestamp ASC;";
				command.CommandText = sql;

				command.Parameters.AddWithValue("$sessionId", sessionId);
				if (hasCategory)
				{
					command.Parameters.AddWithValue("$category", categoryFilter);
				}
				if (hasAction)
				{
					command.Parameters.AddWithValue("$action", actionFilter);
				}
				if (hasResult)
				{
					command.Parameters.AddWithValue("$result", resultFilter);
				}

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					while (reader.Read())
					{
						AnalyticsEventRow row = ReadEventRow(reader);
						rows.Add(row);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return rows;
		}

		/// <summary>
		/// Read every session for a given device id, most recent first by
		/// server-side started_at.
		/// </summary>
		public List<AnalyticsSessionRow> GetSessionsForDevice(string deviceId)
		{
			List<AnalyticsSessionRow> rows = new List<AnalyticsSessionRow>();
			if (string.IsNullOrEmpty(deviceId))
			{
				return rows;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT session_id, device_id, user, app_version, platform, started_at FROM sessions WHERE device_id = $deviceId ORDER BY started_at DESC;";
				command.Parameters.AddWithValue("$deviceId", deviceId);

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					while (reader.Read())
					{
						AnalyticsSessionRow row = ReadSessionRow(reader);
						rows.Add(row);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return rows;
		}

		private AnalyticsEventRow ReadEventRow(SqliteDataReader reader)
		{
			AnalyticsEventRow row = new AnalyticsEventRow();
			row.Id = reader.GetInt64(0);
			row.SessionId = ReadString(reader, 1);
			row.Timestamp = ReadString(reader, 2);
			row.ReceivedAt = ReadString(reader, 3);
			row.Category = ReadString(reader, 4);
			row.Action = ReadString(reader, 5);
			row.Result = ReadString(reader, 6);
			row.ObjectType = ReadString(reader, 7);
			row.ObjectId = ReadString(reader, 8);
			row.DurationMs = ReadLong(reader, 9, -1);
			row.Detail = ReadString(reader, 10);
			return row;
		}

		private AnalyticsSessionRow ReadSessionRow(SqliteDataReader reader)
		{
			AnalyticsSessionRow row = new AnalyticsSessionRow();
			row.SessionId = ReadString(reader, 0);
			row.DeviceId = ReadString(reader, 1);
			row.User = ReadString(reader, 2);
			row.AppVersion = ReadString(reader, 3);
			row.Platform = ReadString(reader, 4);
			row.StartedAt = ReadString(reader, 5);
			return row;
		}

		private string ReadString(SqliteDataReader reader, int columnIndex)
		{
			if (reader.IsDBNull(columnIndex))
			{
				return "";
			}
			return reader.GetString(columnIndex);
		}

		private long ReadLong(SqliteDataReader reader, int columnIndex, long whenNull)
		{
			if (reader.IsDBNull(columnIndex))
			{
				return whenNull;
			}
			return reader.GetInt64(columnIndex);
		}
	}
}
