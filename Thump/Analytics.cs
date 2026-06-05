using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using PulseAPI.CSharp;
using Thump.Data;
using Thump.Pulse;

namespace Thump
{
	/// <summary>
	/// Client side of the product-analytics pipeline: records structured usage
	/// events, buffers them, and ships them to the server in batches. This is
	/// usage telemetry, not logging -- distinct from <see cref="Log"/>, which is
	/// the local diagnostic file writer. Constructed once by MainView with the
	/// media client used to POST; owns its own buffer and background flush
	/// thread the same way the server's AnalyticsDB owns its setup. Reach it
	/// through MainView.Analytics.
	/// </summary>
	public class Analytics
	{
		/// <summary>Per-launch session identifier; constant for the lifetime of the process.</summary>
		private readonly string m_sessionId = System.Guid.NewGuid().ToString();

		/// <summary>Network client used to ship batches to the server.</summary>
		private MediaClient m_client;

		/// <summary>Bounded ring buffer of events awaiting batch POST. Drops oldest when full.</summary>
		private readonly List<PulseAnalyticsEvent> m_eventBuffer = new List<PulseAnalyticsEvent>();

		/// <summary>Guards m_eventBuffer; held only for the add/drop and the drain copy.</summary>
		private readonly object m_bufferLock = new object();

		/// <summary>Wakes the flush thread to drain immediately instead of waiting for the next tick.</summary>
		private readonly AutoResetEvent m_flushSignal = new AutoResetEvent(false);

		/// <summary>Maximum events retained before the oldest is dropped.</summary>
		private const int s_bufferCapacity = 500;

		/// <summary>Buffer length that triggers a flush between scheduled ticks.</summary>
		private const int s_flushHighWater = 50;

		/// <summary>
		/// Stores the client used to ship batches and starts the background flush
		/// thread. The object owns its own setup rather than having it wired in
		/// from outside.
		/// </summary>
		public Analytics(MediaClient client)
		{
			m_client = client;

			Thread flushThread = new Thread(FlushLoop);
			flushThread.IsBackground = true;
			flushThread.Name = "ThumpAnalyticsFlush";
			flushThread.Start();
		}

		/// <summary>
		/// Record a usage event with no detail string. The caller-info default
		/// arguments are the sanctioned exception to the no-default-params rule.
		/// </summary>
		public void Event(eAction action, eResult result, [CallerMemberName] string member = "", [CallerFilePath] string file = "")
		{
			Event(action, result, "", member, file);
		}

		/// <summary>
		/// Record a usage event with a free-form detail string. Always writes a
		/// local breadcrumb to the diagnostic log so the event is visible
		/// on-device even with remote analytics off; additionally enqueues the
		/// event for the server when analytics is enabled. Never throws into the
		/// caller -- recording an event must not destabilise app code.
		/// </summary>
		public void Event(eAction action, eResult result, string detail, [CallerMemberName] string member = "", [CallerFilePath] string file = "")
		{
			string location = Path.GetFileNameWithoutExtension(file) + "." + member;
			string detailSuffix = "";
			if (!string.IsNullOrEmpty(detail))
			{
				detailSuffix = " " + detail;
			}

			Log.Info("[analytics] " + action + " " + result + " " + location + detailSuffix);

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
				record.Location = location;
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
