using PulseAPI.CSharp;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thump.Data;
using System.IO;

namespace Thump.Pulse
{
	public class Diagnostics
	{
		private MediaClient m_client;
		private string m_sessionId;

		public Diagnostics(MediaClient client, string sessionId)
		{
			m_client = client;
			m_sessionId = sessionId;
		}

		public void ReportErrorEvent(string errorMessage, string notes, string filePath, string memberName, List<PulseAnalyticsEvent> eventHistory)
		{
			try
			{
				string caller = Path.GetFileNameWithoutExtension(filePath);
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

				diagEvent.DeviceId = ThumpSettings.GetOrCreateDeviceId();
				diagEvent.SessionId = m_sessionId;
				diagEvent.User = ThumpSettings.GetUsername();
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

				m_client.PostDiagnostics(diagEvent);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[diagnostics] report failed: " + ex.Message);
			}
		}
	}
}