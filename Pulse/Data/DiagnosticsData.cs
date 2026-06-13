using Pulse.DataStorage;
using System;
using System.Collections.Generic;
using System.IO;

namespace Pulse.Data
{
	/// <summary>
	/// In-memory store of recent client diagnostic events, backed by PulseDataStore.
	/// The list is authoritative at runtime; the store is pure persistence. Append
	/// only (each event has its own Id), capped by a rolling retention window. The
	/// only consumer is the diagnostics read endpoint, not Pulse itself.
	/// </summary>
	public class DiagnosticsData
	{
		private List<DiagnosticRecord> m_events = new List<DiagnosticRecord>();
		private object m_lock = new object();
		private PulseDataStore m_data;
		private int m_retentionDays = 30;

		public DiagnosticsData(PulseConfig config)
		{
			string diagnosticsDB = "diagnostics.db";
#if DEBUG
			diagnosticsDB = "diagnostics_staging.db";
#endif
			string dbPath = Path.Combine(config.PulseDataPath, diagnosticsDB);
			m_data = new PulseDataStore(dbPath);
		}

		/// <summary>Hydrate the in-memory list from the store. Call once at startup.</summary>
		public void Load()
		{
			List<DiagnosticRecord> records = m_data.LoadList<DiagnosticRecord>(eDataType.Diagnostic);
			lock (m_lock)
			{
				for (int index = 0; index < records.Count; index++)
				{
					m_events.Add(records[index]);
				}
			}
			PruneOld();
		}

		/// <summary>Append one event, persist it immediately, then drop anything past retention.</summary>
		public void Add(DiagnosticRecord record)
		{
			if (record == null)
			{
				return;
			}
			lock (m_lock)
			{
				m_events.Add(record);
			}
			m_data.Save(eDataType.Diagnostic, record);
			record.m_bIsDirty = false;
			PruneOld();
		}

		/// <summary>
		/// Recent events, newest first by server ReceivedAt. Empty deviceId means all
		/// devices; limit caps the count.
		/// </summary>
		public List<DiagnosticRecord> GetRecent(string deviceId, int limit)
		{
			if (limit <= 0)
			{
				limit = 200;
			}

			List<DiagnosticRecord> snapshot;
			lock (m_lock)
			{
				snapshot = new List<DiagnosticRecord>(m_events);
			}
			snapshot.Sort(CompareByReceivedDescending);

			List<DiagnosticRecord> result = new List<DiagnosticRecord>();
			for (int index = 0; index < snapshot.Count && result.Count < limit; index++)
			{
				if (string.IsNullOrEmpty(deviceId) || snapshot[index].DeviceId == deviceId)
				{
					result.Add(snapshot[index]);
				}
			}
			return result;
		}

		private static int CompareByReceivedDescending(DiagnosticRecord first, DiagnosticRecord second)
		{
			return string.CompareOrdinal(second.Timestamp, first.Timestamp);
		}

		private void PruneOld()
		{
			string cutoff = DateTime.UtcNow.AddDays(-m_retentionDays).ToString("o");

			List<DiagnosticRecord> expired = new List<DiagnosticRecord>();
			lock (m_lock)
			{
				for (int index = m_events.Count - 1; index >= 0; index--)
				{
					if (string.CompareOrdinal(m_events[index].Timestamp, cutoff) < 0)
					{
						expired.Add(m_events[index]);
						m_events.RemoveAt(index);
					}
				}
			}

			for (int index = 0; index < expired.Count; index++)
			{
				m_data.Delete(eDataType.Diagnostic, expired[index].Id);
			}
		}
	}
}
