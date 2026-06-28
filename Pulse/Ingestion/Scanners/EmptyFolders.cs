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
			Log.Info("Cleaning up empty folders...");
			CleanEmptyFolders(m_workingDirectory);
			OnComplete();
			base.DoWork();
		}

		public bool CleanEmptyFolders(string currentDir)
		{
			ReportProgress();
			bool deleteMe = false;
			string[] files = Directory.GetFiles(currentDir);
			string[] subdirs = Directory.GetDirectories(currentDir);

			if (files.Length == 0 && subdirs.Length == 0)
			{
				deleteMe = true;
			}
			else if (files.Length == 0)
			{
				bool allChildrenDeleted = true;
				for (int i = 0; i < subdirs.Length; i++)
				{
					if (!CleanEmptyFolders(subdirs[i]))
					{
						allChildrenDeleted = false;
					}
				}
				deleteMe = allChildrenDeleted;
			}
			else
			{
				for (int i = 0; i < subdirs.Length; i++)
				{
					CleanEmptyFolders(subdirs[i]);
				}
			}

			if (deleteMe)
			{
				File.SetAttributes(currentDir, FileAttributes.Normal);
				Directory.Delete(currentDir);
				RecordInfo("Removed empty folder: " + currentDir);
				return true;
			}
			return false;
		}
	}
}
