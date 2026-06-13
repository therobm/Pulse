using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using PulseAPI.CSharp;

namespace Pulse.Database
{
	/// <summary>
	/// Carrier the route handler hands to the drain queue: the parsed event plus
	/// the server-stamped received_at. received_at lives only on this carrier and
	/// on the row -- it is not part of the public PulseDiagnosticsEvent wire type,
	/// so the client never sends it.
	/// </summary>
	internal class DiagnosticsItem
	{
		public PulseDiagnosticsEvent Event;
		public string ReceivedAt;
	}

	/// <summary>
	/// Intake + drain for the client-diagnostics pipeline. Unlike analytics,
	/// diagnostics are not batched on the client -- each error ships on its own so
	/// the one right before a crash still gets out -- so the route handler enqueues
	/// a single event and the drain thread writes one row per event. The drain
	/// thread also runs a 30-day retention prune at startup and on a 6-hour cadence.
	/// The request thread never touches the database.
	/// </summary>
	public class DiagnosticsDB
	{
		/// <summary>One row in the diagnostics table, in the shape the read endpoint returns it.</summary>
		private class DiagnosticsRow
		{
			public string DeviceId;
			public string SessionId;
			public string AppVersion;
			public int BuildNumber;
			public string User;
			public string Platform;
			public string OsVersion;
			public string DeviceModel;
			public string NetworkType;
			public string Caller;
			public string MemberName;
			public string ErrorMessage;
			public string Detail;
			public string Timestamp;
		}

		private DiagnosticsDBConnector m_connector;
		private BlockingCollection<DiagnosticsItem> m_queue;
		private Thread m_drainThread;
		private TimeSpan m_takeTimeout;
		private TimeSpan m_pruneInterval;
		private int m_retentionDays;
		private DateTime m_lastPruneUtc;

		public DiagnosticsDB(PulseConfig config)
		{
			string environmentName = config.DatabaseEnvironment;
			if (string.IsNullOrWhiteSpace(environmentName))
			{
				environmentName = "Production";
			}
#if DEBUG
			if (!string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase))
			{
				Log.Warning("Debugger attached: forcing Staging environment for diagnostics DB (config said '" + environmentName + "').");
			}
			environmentName = "Staging";
#endif

			if (!Directory.Exists(config.PulseDataPath))
			{
				Directory.CreateDirectory(config.PulseDataPath);
			}

			string diagnosticsFileName = "pulse_diagnostics_" + environmentName.ToLowerInvariant() + ".db";
			string diagnosticsPath = Path.Combine(config.PulseDataPath, diagnosticsFileName);

			m_connector = new DiagnosticsDBConnector();
			m_connector.SetDatabaseFilePath(diagnosticsPath);

			DiagnosticsDBMigrations migrations = new DiagnosticsDBMigrations(m_connector);
			migrations.RunMigrations();
			Log.Info("Pulse diagnostics DB: env=" + environmentName + " path=" + diagnosticsPath);

			m_queue = new BlockingCollection<DiagnosticsItem>();
			m_takeTimeout = TimeSpan.FromMinutes(1);
			m_pruneInterval = TimeSpan.FromHours(6);
			m_retentionDays = 30;
			m_lastPruneUtc = DateTime.MinValue;

			m_drainThread = new Thread(DrainLoop);
			m_drainThread.IsBackground = true;
			m_drainThread.Name = "PulseDiagnosticsDrain";
			m_drainThread.Start();
		}

		/// <summary>
		/// Called from the request thread. Wraps the parsed event with the
		/// server-stamped received_at and adds it to the drain queue. Does not
		/// touch SQLite.
		/// </summary>
		public void Enqueue(PulseDiagnosticsEvent diagnosticsEvent, string receivedAt)
		{
			if (diagnosticsEvent == null)
			{
				return;
			}
			DiagnosticsItem item = new DiagnosticsItem();
			item.Event = diagnosticsEvent;
			item.ReceivedAt = receivedAt;
			m_queue.Add(item);
		}

		private void DrainLoop()
		{
			TryPrune();

			while (true)
			{
				try
				{
					DiagnosticsItem item;
					bool gotItem = m_queue.TryTake(out item, m_takeTimeout);
					if (gotItem)
					{
						if (item != null)
						{
							WriteEvent(item);
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
					Log.Error("DiagnosticsDB drain failed: " + ex.Message);
				}
			}
		}

		private void WriteEvent(DiagnosticsItem item)
		{
			PulseDiagnosticsEvent diagnosticsEvent = item.Event;
			if (diagnosticsEvent == null)
			{
				return;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "INSERT INTO diagnostics (device_id, session_id, app_version, build_number, user, platform, os_version, device_model, network_type, caller, member_name, error_message, detail, timestamp, received_at) VALUES ($deviceId, $sessionId, $appVersion, $buildNumber, $user, $platform, $osVersion, $deviceModel, $networkType, $caller, $memberName, $errorMessage, $detail, $timestamp, $receivedAt);";

				command.Parameters.AddWithValue("$deviceId", TextOrEmpty(diagnosticsEvent.DeviceId));
				command.Parameters.AddWithValue("$sessionId", TextOrEmpty(diagnosticsEvent.SessionId));
				command.Parameters.AddWithValue("$appVersion", TextOrEmpty(diagnosticsEvent.AppVersion));
				command.Parameters.AddWithValue("$buildNumber", diagnosticsEvent.BuildNumber);
				command.Parameters.AddWithValue("$user", TextOrEmpty(diagnosticsEvent.User));
				command.Parameters.AddWithValue("$platform", TextOrEmpty(diagnosticsEvent.Platform));
				command.Parameters.AddWithValue("$osVersion", TextOrEmpty(diagnosticsEvent.OsVersion));
				command.Parameters.AddWithValue("$deviceModel", TextOrEmpty(diagnosticsEvent.DeviceModel));
				command.Parameters.AddWithValue("$networkType", TextOrEmpty(diagnosticsEvent.NetworkType));
				command.Parameters.AddWithValue("$caller", TextOrEmpty(diagnosticsEvent.Caller));
				command.Parameters.AddWithValue("$memberName", TextOrEmpty(diagnosticsEvent.MemberName));
				command.Parameters.AddWithValue("$errorMessage", TextOrEmpty(diagnosticsEvent.ErrorMessage));
				command.Parameters.AddWithValue("$detail", TextOrEmpty(diagnosticsEvent.Detail));
				command.Parameters.AddWithValue("$timestamp", TextOrEmpty(diagnosticsEvent.Timestamp));
				command.Parameters.AddWithValue("$receivedAt", TextOrEmpty(item.ReceivedAt));

				command.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				Log.Error("DiagnosticsDB.WriteEvent failed: " + ex.Message);
			}
			finally
			{
				connection.Close();
			}
		}

		private void TryPrune()
		{
			try
			{
				PruneOldEvents();
			}
			catch (Exception ex)
			{
				Log.Error("DiagnosticsDB prune failed: " + ex.Message);
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
				command.CommandText = "DELETE FROM diagnostics WHERE received_at < $cutoff;";
				command.Parameters.AddWithValue("$cutoff", cutoff);
				int deleted = command.ExecuteNonQuery();
				if (deleted > 0)
				{
					Log.Info("Diagnostics retention prune removed " + deleted + " event(s) older than " + cutoff);
				}
			}
			finally
			{
				connection.Close();
			}
		}

		/// <summary>
		/// Read recent diagnostics, newest first by server-side received_at. An
		/// empty deviceId means "all devices"; limit caps the row count.
		/// </summary>
		public List<PulseDiagnosticsEvent> GetRecent(string deviceId, int limit)
		{
			List<PulseDiagnosticsEvent> rows = new List<PulseDiagnosticsEvent>();
			if (limit <= 0)
			{
				limit = 200;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				bool hasDevice = !string.IsNullOrEmpty(deviceId);

				string sql = "SELECT device_id, session_id, app_version, build_number, user, platform, os_version, device_model, network_type, caller, member_name, error_message, detail, timestamp FROM diagnostics";
				if (hasDevice)
				{
					sql = sql + " WHERE device_id = $deviceId";
				}
				sql = sql + " ORDER BY received_at DESC LIMIT $limit;";
				command.CommandText = sql;

				if (hasDevice)
				{
					command.Parameters.AddWithValue("$deviceId", deviceId);
				}
				command.Parameters.AddWithValue("$limit", limit);

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					while (reader.Read())
					{
						DiagnosticsRow row = ReadRow(reader);

						PulseDiagnosticsEvent evt = new PulseDiagnosticsEvent();
						evt.DeviceId = row.DeviceId;
						evt.SessionId = row.SessionId;
						evt.AppVersion = row.AppVersion;
						evt.BuildNumber = row.BuildNumber;
						evt.User = row.User;
						evt.Platform = row.Platform;
						evt.OsVersion = row.OsVersion;
						evt.DeviceModel = row.DeviceModel;
						evt.NetworkType = row.NetworkType;
						evt.Caller = row.Caller;
						evt.MemberName = row.MemberName;
						evt.ErrorMessage = row.ErrorMessage;
						evt.Detail = row.Detail;
						evt.Timestamp = row.Timestamp;

						rows.Add(evt);
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

		private DiagnosticsRow ReadRow(SqliteDataReader reader)
		{
			DiagnosticsRow row = new DiagnosticsRow();
			row.DeviceId = ReadString(reader, 0);
			row.SessionId = ReadString(reader, 1);
			row.AppVersion = ReadString(reader, 2);
			row.BuildNumber = ReadInt(reader, 3, 0);
			row.User = ReadString(reader, 4);
			row.Platform = ReadString(reader, 5);
			row.OsVersion = ReadString(reader, 6);
			row.DeviceModel = ReadString(reader, 7);
			row.NetworkType = ReadString(reader, 8);
			row.Caller = ReadString(reader, 9);
			row.MemberName = ReadString(reader, 10);
			row.ErrorMessage = ReadString(reader, 11);
			row.Detail = ReadString(reader, 12);
			row.Timestamp = ReadString(reader, 13);
			return row;
		}

		private string TextOrEmpty(string value)
		{
			if (value == null)
			{
				return "";
			}
			return value;
		}

		private string ReadString(SqliteDataReader reader, int columnIndex)
		{
			if (reader.IsDBNull(columnIndex))
			{
				return "";
			}
			return reader.GetString(columnIndex);
		}

		private int ReadInt(SqliteDataReader reader, int columnIndex, int whenNull)
		{
			if (reader.IsDBNull(columnIndex))
			{
				return whenNull;
			}
			return reader.GetInt32(columnIndex);
		}
	}
}
