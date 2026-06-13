using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using PulseAPI.CSharp;
using Thump.Data;

namespace Thump.Pulse
{

	public class Analytics
	{
		private readonly string m_sessionId = System.Guid.NewGuid().ToString();


		public Diagnostics m_diagnostics;
		private MediaClient m_client;

		private readonly List<PulseAnalyticsEvent> m_eventBuffer = new List<PulseAnalyticsEvent>();
		private readonly List<PulseAnalyticsEvent> m_eventHistory = new List<PulseAnalyticsEvent>();

		private readonly object m_bufferLock = new object();

		private readonly AutoResetEvent m_flushSignal = new AutoResetEvent(false);

		private const int s_bufferCapacity = 500;
		private const int s_historyCapacity = 50;

		private const int s_flushHighWater = 50;

		private const long s_noDuration = -1;

		private readonly object m_analyticsFileLock = new object();

		private string m_analyticsLogPath = "";

		
		public Analytics(MediaClient client)
		{
			m_client = client;

			m_diagnostics = new Diagnostics(client, m_sessionId);

			InitAnalyticsLogFile();

			Thread flushThread = new Thread(FlushLoop);
			flushThread.IsBackground = true;
			flushThread.Name = "ThumpAnalyticsFlush";
			flushThread.Start();
		}

		private void InitAnalyticsLogFile()
		{
			lock (m_analyticsFileLock)
			{
				try
				{
					string directory = FileSystem.AppDataDirectory;
					if (!Directory.Exists(directory))
					{
						Directory.CreateDirectory(directory);
					}
					m_analyticsLogPath = Path.Combine(directory, "thump-analytics.log");
					File.WriteAllText(m_analyticsLogPath, "");
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[analytics] log file init failed: " + ex.Message);
				}
			}
		}

		public string GetAnalyticsLogFilePath()
		{
			return m_analyticsLogPath;
		}

		private void WriteAnalyticsLogLine(string crumb)
		{
			if (string.IsNullOrEmpty(m_analyticsLogPath))
			{
				return;
			}
			string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + crumb;
			lock (m_analyticsFileLock)
			{
				try
				{
					File.AppendAllText(m_analyticsLogPath, line + Environment.NewLine);
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[analytics] log file write failed: " + ex.Message);
				}
			}
		}


		public void Event(eAction action, eResult result, ePulseWireType objectType = ePulseWireType.Invalid, string objectId = "", long durationMs = s_noDuration, string detail = "")
		{
			Record(action, result, ObjectTypeName(objectType), objectId, durationMs, detail);
		}

		/// <summary>
		/// Path for diagnostic error events
		/// </summary>
		public void DiagnosticEvent(string errorMessage, string notes, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
		{
			List<PulseAnalyticsEvent> historyCopy = null;
			lock(m_bufferLock)
			{
				historyCopy = new List<PulseAnalyticsEvent>(m_eventHistory);
			}
			m_diagnostics.ReportErrorEvent(errorMessage, notes, filePath, memberName, historyCopy);
		}

		private static string ObjectTypeName(ePulseWireType objectType)
		{
			return objectType.ToString().ToLowerInvariant();
		}

		private void Record(eAction action, eResult result, string objectType, string objectId, long durationMs, string detail)
		{
			string crumb = "[analytics] " + action + " " + result;
			if (!string.IsNullOrEmpty(objectId))
			{
				crumb = crumb + " " + objectType + "/" + objectId;
			}
			if (durationMs >= 0)
			{
				crumb = crumb + " " + durationMs + "ms";
			}
			if (!string.IsNullOrEmpty(detail))
			{
				crumb = crumb + " " + detail;
			}
			Log.Info(crumb);
			WriteAnalyticsLogLine(crumb);

			try
			{
				bool enabled = false;
				try
				{
					enabled = ThumpSettings.GetAnalyticsEnabled();
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[analytics] enabled probe failed: " + ex.Message);
					return;
				}
				if (!enabled)
				{
					return;
				}

				PulseAnalyticsEvent record = new PulseAnalyticsEvent();
				record.Action = action;
				record.Result = result;
				record.ObjectType = objectType;
				record.ObjectId = objectId;
				record.DurationMs = durationMs;
				record.Detail = detail;
				// Round-trip "o" UTC so the server can order and prune correctly.
				record.Timestamp = System.DateTime.UtcNow.ToString("o");

				bool signalFlush = false;
				lock (m_bufferLock)
				{
					if (m_eventBuffer.Count >= s_bufferCapacity)
					{
						m_eventBuffer.RemoveAt(0);
					}
					m_eventBuffer.Add(record);

					if (m_eventHistory.Count >= s_historyCapacity)
					{
						m_eventHistory.RemoveAt(0);
					}
					m_eventHistory.Add(record);
					
					if (m_eventBuffer.Count >= s_flushHighWater)
					{
						signalFlush = true;
					}
				}
				if (result == eResult.Fail)
				{
					signalFlush = true;
				}
				if (signalFlush)
				{
					m_flushSignal.Set();
				}
			}
			catch (Exception ex)
			{
				// Recording must never throw into callers. Debug only -- never
				// route a failure back through the analytics path.
				Debug.WriteLine("[analytics] enqueue failed: " + ex.Message);
			}
		}

		private void FlushLoop()
		{
			// Sanctioned infinite background loop -- runs for the process lifetime.
			while (true)
			{
				try
				{
					m_flushSignal.WaitOne(TimeSpan.FromSeconds(10));
					FlushOnce();
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[analytics] flush loop iteration failed: " + ex.Message);
				}
			}
		}

		private void FlushOnce()
		{
			try
			{
				if (m_client == null)
				{
					return;
				}
				if (!ThumpSettings.GetAnalyticsEnabled())
				{
					return;
				}

				List<PulseAnalyticsEvent> drained = new List<PulseAnalyticsEvent>();
				lock (m_bufferLock)
				{
					int count = m_eventBuffer.Count;
					if (count == 0)
					{
						return;
					}
					for (int index = 0; index < count; index++)
					{
						drained.Add(m_eventBuffer[index]);
					}
					m_eventBuffer.Clear();
				}

				PulseAnalyticsBatch batch = new PulseAnalyticsBatch();
				batch.DeviceId = ThumpSettings.GetOrCreateDeviceId();
				batch.SessionId = m_sessionId;
				batch.User = ThumpSettings.GetUsername();
				batch.AppVersion = "";
				try
				{
					batch.AppVersion = AppInfo.VersionString;
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[analytics] AppInfo.VersionString unavailable: " + ex.Message);
					batch.AppVersion = "";
				}
				batch.Platform = "Android";
				try
				{
					batch.Platform = DeviceInfo.Platform.ToString();
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[analytics] DeviceInfo.Platform unavailable: " + ex.Message);
					batch.Platform = "Android";
				}
				batch.Events = drained;

				// Fire-and-forget; do not re-enqueue on failure. Dropped
				// analytics are acceptable.
				m_client.PostAnalytics(batch);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[analytics] FlushOnce failed: " + ex.Message);
			}
		}
	}
}
