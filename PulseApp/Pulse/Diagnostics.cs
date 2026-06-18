using PulseAPI.CSharp;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PulseApp.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace PulseApp.Pulse
{
	public class Diagnostics
	{
		private const int s_maxQueuedEvents = 200;
		private const string s_queueFileName = "diagnostics_queue.json";

		private MediaClient m_client;
		private string m_sessionId;
		private string m_queuePath = "";
		private readonly object m_queueLock = new object();
		private bool m_draining = false;

		public Diagnostics(MediaClient client, string sessionId)
		{
			m_client = client;
			m_sessionId = sessionId;
			m_queuePath = Path.Combine(FileSystem.AppDataDirectory, s_queueFileName);
			Connectivity.ConnectivityChanged += OnConnectivityChanged;
		}

		private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs args)
		{
			if (args.NetworkAccess == NetworkAccess.Internet)
			{
				StartDrain();
			}
		}

		private void StartDrain()
		{
			Task.Run(Drain);
		}

		/// <summary>
		/// Upload queued events oldest-first, stopping at the first failure so a
		/// dead network leaves the rest on disk for the next attempt. Removes only
		/// the prefix that actually sent, so events appended mid-drain survive.
		/// </summary>
		private void Drain()
		{
			lock (m_queueLock)
			{
				if (m_draining)
				{
					return;
				}
				m_draining = true;
			}
			try
			{
				List<PulseDiagnosticsEvent> snapshot;
				lock (m_queueLock)
				{
					snapshot = LoadQueue();
				}
				int sentCount = 0;
				for (int index = 0; index < snapshot.Count; index++)
				{
					bool sent = m_client.PostDiagnostics(snapshot[index]);
					if (!sent)
					{
						break;
					}
					sentCount++;
				}
				if (sentCount > 0)
				{
					lock (m_queueLock)
					{
						List<PulseDiagnosticsEvent> queue = LoadQueue();
						int removeCount = sentCount;
						if (removeCount > queue.Count)
						{
							removeCount = queue.Count;
						}
						queue.RemoveRange(0, removeCount);
						SaveQueue(queue);
					}
				}
			}
			finally
			{
				lock (m_queueLock)
				{
					m_draining = false;
				}
			}
		}

		private void Enqueue(PulseDiagnosticsEvent diagEvent)
		{
			lock (m_queueLock)
			{
				List<PulseDiagnosticsEvent> queue = LoadQueue();
				queue.Add(diagEvent);
				if (queue.Count > s_maxQueuedEvents)
				{
					queue.RemoveRange(0, queue.Count - s_maxQueuedEvents);
				}
				SaveQueue(queue);
			}
		}

		private List<PulseDiagnosticsEvent> LoadQueue()
		{
			if (!File.Exists(m_queuePath))
			{
				return new List<PulseDiagnosticsEvent>();
			}
			try
			{
				string data = File.ReadAllText(m_queuePath);
				if (string.IsNullOrEmpty(data))
				{
					return new List<PulseDiagnosticsEvent>();
				}
				List<PulseDiagnosticsEvent> list = PulseWire.ParseList<PulseDiagnosticsEvent>(data);
				if (list == null)
				{
					return new List<PulseDiagnosticsEvent>();
				}
				return list;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[diagnostics] queue load failed: " + ex.Message);
				return new List<PulseDiagnosticsEvent>();
			}
		}

		private void SaveQueue(List<PulseDiagnosticsEvent> queue)
		{
			try
			{
				string data = PulseWire.Serialize(queue);
				File.WriteAllText(m_queuePath, data);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[diagnostics] queue save failed: " + ex.Message);
			}
		}

		public void ReportErrorEvent(string errorMessage, string notes, string filePath, string memberName, List<PulseAnalyticsEvent> eventHistory)
		{
			try
			{
				string caller = GetFileName(filePath);
				StringBuilder detail = new StringBuilder();
				if (!string.IsNullOrEmpty(notes))
				{
					detail.AppendLine("NOTES: " + notes);
				}

				if (eventHistory != null && eventHistory.Count > 0)
				{
					detail.AppendLine("--- RECENT EVENTS (" + eventHistory.Count + ") ---");
					for (int index = 0; index < eventHistory.Count; index++)
					{
						PulseAnalyticsEvent ev = eventHistory[index];
						detail.Append(ev.Timestamp + " " + ev.Action + " " + ev.Result);
						if (!string.IsNullOrEmpty(ev.ObjectId))
						{
							detail.Append(" " + ev.ObjectType + "/" + ev.ObjectId);
						}
						if (ev.DurationMs >= 0)
						{
							detail.Append(" " + ev.DurationMs + "ms");
						}
						if (!string.IsNullOrEmpty(ev.Detail))
						{
							detail.Append(" " + ev.Detail);
						}
						detail.AppendLine();
					}
				}

				PulseDiagnosticsEvent diagEvent = new PulseDiagnosticsEvent();

				diagEvent.DeviceId = PulseAppSettings.GetOrCreateDeviceId();
				diagEvent.SessionId = m_sessionId;
				diagEvent.User = PulseAppSettings.GetUsername();
				diagEvent.AppVersion = "";
				diagEvent.ErrorMessage = errorMessage;
				diagEvent.Detail = detail.ToString();
				diagEvent.Timestamp = DateTime.UtcNow.ToString("o");
				diagEvent.Caller = caller;
				diagEvent.MemberName = memberName;
			
				try
				{
					diagEvent.AppVersion = AppInfo.VersionString;
				}
				catch
				{
					diagEvent.AppVersion = "";
				}
				diagEvent.Platform = "Android";
				try
				{
					diagEvent.Platform = DeviceInfo.Platform.ToString();
				}
				catch
				{
					diagEvent.Platform = "Android";
				}

				diagEvent.BuildNumber = 0;
				try
				{
					int parsedBuild = 0;
					if (int.TryParse(AppInfo.BuildString, out parsedBuild))
					{
						diagEvent.BuildNumber = parsedBuild;
					}
				}
				catch
				{
					diagEvent.BuildNumber = 0;
				}

				diagEvent.OsVersion = "";
				try
				{
					diagEvent.OsVersion = DeviceInfo.VersionString;
				}
				catch
				{
					diagEvent.OsVersion = "";
				}

				diagEvent.DeviceModel = "";
				try
				{
					diagEvent.DeviceModel = DeviceInfo.Manufacturer + " " + DeviceInfo.Model;
				}
				catch
				{
					diagEvent.DeviceModel = "";
				}

				diagEvent.NetworkType = "unknown";
				try
				{
					bool hasCellular = false;
					bool hasWifi = false;
					bool hasEthernet = false;
					foreach (ConnectionProfile profile in Connectivity.Current.ConnectionProfiles)
					{
						if (profile == ConnectionProfile.Cellular)
						{
							hasCellular = true;
						}
						if (profile == ConnectionProfile.WiFi)
						{
							hasWifi = true;
						}
						if (profile == ConnectionProfile.Ethernet)
						{
							hasEthernet = true;
						}
					}
					if (hasCellular)
					{
						diagEvent.NetworkType = "cellular";
					}
					else if (hasWifi)
					{
						diagEvent.NetworkType = "wifi";
					}
					else if (hasEthernet)
					{
						diagEvent.NetworkType = "ethernet";
					}
					else
					{
						diagEvent.NetworkType = "none";
					}
				}
				catch
				{
					diagEvent.NetworkType = "unknown";
				}

				Enqueue(diagEvent);
				StartDrain();
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[diagnostics] report failed: " + ex.Message);
			}
		}

		private string GetFileName(string filePath)
		{
			int separatorIndex = filePath.LastIndexOfAny(new char[] { '\\', '/' });
			string caller = filePath;
			if (separatorIndex >= 0)
			{
				caller = filePath.Substring(separatorIndex + 1);
			}
			int dotIndex = caller.LastIndexOf('.');
			if (dotIndex > 0)
			{
				caller = caller.Substring(0, dotIndex);
			}
			return caller;
		}
	}
}