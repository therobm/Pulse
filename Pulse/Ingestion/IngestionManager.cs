using System;
using System.Collections.Generic;
using System.Threading;
using Pulse.MusicLibrary;
using PulseIngestion;
using PulseIngestion.Reporting;
using PulseIngestion.Scanners;

namespace Pulse.Ingestion
{
	public class IngestionManager
	{
		private PulseConfig m_config;
		private Thread m_thread;
		private List<Scanner> m_scanners;
		private volatile bool m_isIngesting;
		private bool m_bIsRunning;

		public IngestionManager(PulseConfig config)
		{
			m_config = config;
		}

		public void Run()
		{
			if (m_thread != null)
			{
				return;
			}

			IngestionConfig config = m_config.IngestionConfiguration;
			if (!config.ValidateConfig())
			{
				return;
			}
			if (config.ScanningIntervalMinutes <= 0)
			{
				Log.Warning("Ingestion: disabled, ScanningIntervalMinutes is " + config.ScanningIntervalMinutes + ".");
				return;
			}

			m_scanners = BuildScanners(config);

			m_thread = new Thread(RunLoop);
			m_thread.IsBackground = true;
			m_thread.Name = "Pulse.Ingestion";
			m_thread.Start();
			Log.Info("Ingestion: scheduled every " + config.ScanningIntervalMinutes + " minutes.");
		}

		public bool IsIngesting()
		{
			return m_isIngesting;
		}
		private List<Scanner> BuildScanners(IngestionConfig config)
		{
			List<Scanner> scanners = new List<Scanner>();
			scanners.Add(new EmptyFolders(config));
			scanners.Add(new Deduplicate(config, m_config.MusicPath));
			scanners.Add(new MediaConversion(config));
			scanners.Add(new OrganizeLibrary(m_config.MusicPath, config));
			for (int i = 0; i < scanners.Count; i++)
			{
				scanners[i].Initialize();
			}
			return scanners;
		}

		private void RunLoop()
		{
			m_bIsRunning = true;
			while (m_bIsRunning)
			{
				RunPass();
				Thread.Sleep(TimeSpan.FromMinutes(m_config.IngestionConfiguration.ScanningIntervalMinutes));
			}
		}

		private void RunPass()
		{
			m_isIngesting = true;
			try
			{
				Report report = new Report();
				report.MarkStarted();
				for (int i = 0; i < m_scanners.Count; i++)
				{
					m_scanners[i].Pump(report);
				}
				report.MarkFinished();
				report.Write(m_config.IngestionConfiguration);
				Log.Info("Ingestion: pass complete, rescanning library.");
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			finally
			{
				m_isIngesting = false;
			}
		}
	}
}
