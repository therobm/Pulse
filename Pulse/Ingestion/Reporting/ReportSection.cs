using System;
using System.Collections.Generic;

namespace PulseIngestion.Reporting
{
	public class ReportSection
	{
		public string Name;
		public TimeSpan Duration;
		public bool PinToTop = false;
		public List<ReportItem> Items = new List<ReportItem>();

		readonly object m_lock = new object();

		public ReportSection(string name)
		{
			Name = name;
		}

		/// <summary>
		/// Locked because MediaConversion records from concurrent conversion tasks.
		/// </summary>
		public void Add(eReportLevel level, string message)
		{
			lock (m_lock)
			{
				Items.Add(new ReportItem(level, message));
			}
		}

		public int CountOf(eReportLevel level)
		{
			int count = 0;
			for (int i = 0; i < Items.Count; i++)
			{
				if (Items[i].Level == level)
				{
					count++;
				}
			}
			return count;
		}
	}
}
