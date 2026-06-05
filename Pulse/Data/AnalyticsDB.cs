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
	/// One row in the analytics sessions table, in the shape the
	/// /analytics read endpoint returns it. Plain public fields; serialized
	/// through PulseWire which emits field names verbatim.
	/// </summary>
	public class analyticsSessionRow
	{
		public string SessionId;
		public string DeviceId;
		public string User;
		public string AppVersion;
		public string Platform;
		public string StartedAt;
	}

	/// <summary>
	/// One row in the analytics log_events table, in the shape the
	/// /analytics read endpoint returns it.
	/// </summary>
	public class analyticsEventRow
	{
		public long Id;
		public string SessionId;
		public string Timestamp;
		public string Action;
		public string Location;
		public string Result;
		public string Detail;
		public string ReceivedAt;
	}

	/// <summary>
	/// Carrier the route handler hands to the drain queue: the parsed batch
	/// plus the server-stamped received_at string. The received_at lives only
	/// on this carrier -- it is NOT part of the public PulseLogBatch wire type,
	/// so the client never sees or sends it.
	/// </summary>
	internal class AnalyticsBatchItem
	{
		public PulseLogBatch Batch;
		public string ReceivedAt;
	}

	/// <summary>
	/// Intake + drain ("firehose") for the analytics log pipeline. The route
	/// handler calls Enqueue from the request thread; a single background
	/// drain thread pulls batches off the queue, opens one transaction per
	/// batch, upserts the session row, and inserts every event in the batch.
	/// The drain thread also runs a 30-day retention prune at startup and on
	/// a 6-hour cadence after that. The request thread never touches the
	/// database.
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
			m_drainThread.Name = "PulseanalyticsDrain";
			m_drainThread.Start();
		}

		/// <summary>
		/// Called from the request thread. Wraps the parsed batch with the
		/// server-stamped received_at and adds it to the drain queue. Does
		/// not touch SQLite.
		/// </summary>
		public void Enqueue(PulseLogBatch batch, string receivedAt)
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
					Log.Error(-1, "analyticsStore drain failed: " + ex.Message);
				}
			}
		}

		private void WriteBatch(AnalyticsBatchItem item)
		{
			PulseLogBatch batch = item.Batch;
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
							PulseLogEvent logEvent = batch.Events[eventIndex];
							if (logEvent == null)
							{
								continue;
							}
							InsertEvent(connection, transaction, batch.SessionId, logEvent, item.ReceivedAt);
						}
					}

					transaction.Commit();
				}
				catch (Exception ex)
				{
					transaction.Rollback();
					Log.Error(-1, "analyticsStore.WriteBatch failed: " + ex.Message);
				}
			}
			finally
			{
				connection.Close();
			}
		}

		private void UpsertSession(SqliteConnection connection, SqliteTransaction transaction, PulseLogBatch batch, string receivedAt)
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

		private void InsertEvent(SqliteConnection connection, SqliteTransaction transaction, string sessionId, PulseLogEvent logEvent, string receivedAt)
		{
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "INSERT INTO log_events (session_id, timestamp, action, location, result, detail, received_at) VALUES ($sessionId, $timestamp, $action, $location, $result, $detail, $receivedAt);";

			string timestamp = "";
			if (logEvent.Timestamp != null)
			{
				timestamp = logEvent.Timestamp;
			}
			string location = "";
			if (logEvent.Location != null)
			{
				location = logEvent.Location;
			}
			string detail = "";
			if (logEvent.Detail != null)
			{
				detail = logEvent.Detail;
			}

			command.Parameters.AddWithValue("$sessionId", sessionId);
			command.Parameters.AddWithValue("$timestamp", timestamp);
			command.Parameters.AddWithValue("$action", logEvent.Action.ToString());
			command.Parameters.AddWithValue("$location", location);
			command.Parameters.AddWithValue("$result", logEvent.Result.ToString());
			command.Parameters.AddWithValue("$detail", detail);
			command.Parameters.AddWithValue("$receivedAt", receivedAt);
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
				Log.Error(-1, "analyticsStore prune failed: " + ex.Message);
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
				command.CommandText = "DELETE FROM log_events WHERE received_at < $cutoff;";
				command.Parameters.AddWithValue("$cutoff", cutoff);
				int deleted = command.ExecuteNonQuery();
				if (deleted > 0)
				{
					Log.Info(-1, "analytics retention prune removed " + deleted + " event(s) older than " + cutoff);
				}
			}
			finally
			{
				connection.Close();
			}
		}

		/// <summary>
		/// Read every event for a given session, ordered by client timestamp.
		/// Empty action / result filters mean "no filter on that column".
		/// </summary>
		public List<analyticsEventRow> GetEventsForSession(string sessionId, string actionFilter, string resultFilter)
		{
			List<analyticsEventRow> rows = new List<analyticsEventRow>();
			if (string.IsNullOrEmpty(sessionId))
			{
				return rows;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				bool hasAction = !string.IsNullOrEmpty(actionFilter);
				bool hasResult = !string.IsNullOrEmpty(resultFilter);

				string sql = "SELECT id, session_id, timestamp, action, location, result, detail, received_at FROM log_events WHERE session_id = $sessionId";
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
						analyticsEventRow row = ReadEventRow(reader);
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
		public List<analyticsSessionRow> GetSessionsForDevice(string deviceId)
		{
			List<analyticsSessionRow> rows = new List<analyticsSessionRow>();
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
						analyticsSessionRow row = ReadSessionRow(reader);
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

		private analyticsEventRow ReadEventRow(SqliteDataReader reader)
		{
			analyticsEventRow row = new analyticsEventRow();
			row.Id = reader.GetInt64(0);
			row.SessionId = ReadString(reader, 1);
			row.Timestamp = ReadString(reader, 2);
			row.Action = ReadString(reader, 3);
			row.Location = ReadString(reader, 4);
			row.Result = ReadString(reader, 5);
			row.Detail = ReadString(reader, 6);
			row.ReceivedAt = ReadString(reader, 7);
			return row;
		}

		private analyticsSessionRow ReadSessionRow(SqliteDataReader reader)
		{
			analyticsSessionRow row = new analyticsSessionRow();
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
	}
}
