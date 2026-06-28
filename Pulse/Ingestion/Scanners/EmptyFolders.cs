using System;
using System.IO;
using System.Linq;

namespace PulseIngestion.Scanners
{
	public class EmptyFolders : Scanner
	{

		public EmptyFolders(IngestionConfig config) : base(config)
		{
			m_bIsActive = m_config.RemoveEmptyDirectories;
		}

		public override void Initialize()
		{
			m_workingDirectory = m_config.MusicImportDir;

			base.Initialize();
		}
		protected override void DoWork()
		{
			CleanEmptyFolders();
			base.DoWork();
		}
		public void CleanEmptyFolders()
		{
			Log.Info("Cleaning up empty folders...");
			foreach (string folder in Directory.EnumerateDirectories(m_workingDirectory, "*.*", SearchOption.AllDirectories))
			{
				ReportProgress();
				if (Directory.EnumerateFileSystemEntries(folder).Any())
				{
					continue;
				}
				Directory.Delete(folder);
				RecordInfo("Removed empty folder: " + folder);
			}
			OnComplete();
		}
	}
}
