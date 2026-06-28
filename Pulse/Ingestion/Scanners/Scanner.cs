using Pulse;
using PulseIngestion.Reporting;
using System;
using System.Diagnostics;

namespace PulseIngestion.Scanners
{
	public abstract class Scanner
	{
		protected string m_workingDirectory;
		protected IngestionConfig m_config;
		protected bool m_bIsActive = false;
		protected string m_scannerName;
		protected ReportSection m_section;
		private Stopwatch m_stopwatch;
		public Scanner(IngestionConfig config)
		{
			m_config = config;
			m_scannerName = GetType().Name;
			m_stopwatch = new Stopwatch();
		}
		public virtual void Initialize()
		{

		}

		public void Pump(Report report)
		{
			if (!m_bIsActive)
			{
				return;
			}
			m_section = report.AddSection(m_scannerName);
			m_section.PinToTop = PinSectionToTop();
			Log.Info(m_scannerName + " started");
			m_stopwatch.Restart();
			DoWork();
		}
		protected virtual void DoWork()
		{

		}
		/// <summary>
		/// Override to render this scanner's section at the top of the report.
		/// </summary>
		protected virtual bool PinSectionToTop()
		{
			return false;
		}
		protected void ReportProgress()
		{
			Console.Write(".");
		}
		protected void RecordInfo(string message)
		{
			Console.WriteLine(message);
			if (m_section != null)
			{
				m_section.Add(eReportLevel.Info, message);
			}
		}
		protected void RecordWarning(string message)
		{
			Console.WriteLine(message);
			if (m_section != null)
			{
				m_section.Add(eReportLevel.Warning, message);
			}
		}
		protected void RecordError(string message)
		{
			Console.WriteLine(message);
			if (m_section != null)
			{
				m_section.Add(eReportLevel.Error, message);
			}
		}
		protected void OnComplete()
		{
			Console.WriteLine(".");
			m_stopwatch.Stop();
			if (m_section != null)
			{
				m_section.Duration = m_stopwatch.Elapsed;
			}
			Log.Info(m_scannerName + " completed in " + m_stopwatch.Elapsed.ToString());
		}
	}
}
