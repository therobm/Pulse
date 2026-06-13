using PulseAPI.CSharp;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thump.Data;

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

		public void ReportErrorEvent(string errorMessage, string notes, string errorType, string location, List<PulseAnalyticsEvent> eventHistory)
		{
			try
			{
				StringBuilder detail = new StringBuilder();
				detail.AppendLine("ERROR: " + errorMessage);
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
				diagEvent.ErrorType = errorType;
				diagEvent.Location = location;
				diagEvent.Detail = detail.ToString();
				diagEvent.Timestamp = DateTime.UtcNow.ToString("o");

			
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

				m_client.PostDiagnostics(diagEvent);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[diagnostics] report failed: " + ex.Message);
			}
		}
	}
}