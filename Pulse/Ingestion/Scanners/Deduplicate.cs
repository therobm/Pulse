

using System;
using System.IO;

namespace PulseIngestion.Scanners
{
	public class Deduplicate : Scanner
	{
		string m_wildcardToken;
		string m_musicRoot;
		public Deduplicate(IngestionConfig config, string musicRoot) : base(config)
		{
			m_musicRoot = musicRoot;
			m_bIsActive = m_config.CleanupDuplicatesByWildcard;
			m_wildcardToken = m_config.DuplicateFileWildcardToken;
		}
		public override void Initialize()
		{
			base.Initialize();
		}
		protected override void DoWork()
		{
			DeduplicateFiles();
			base.DoWork();
		}
		protected void DeduplicateFiles()
		{
			foreach (string f in Directory.EnumerateFiles(m_musicRoot, "*.*", SearchOption.AllDirectories))
			{
				ReportProgress();
				if (m_config.CleanupDuplicatesByWildcard && f.Contains(m_wildcardToken))
				{
					string dir = Path.GetDirectoryName(f);
					string fileName = Path.GetFileName(f);
					string goodName = Path.Combine(dir, fileName.Replace(m_config.DuplicateFileWildcardToken, ""));
					if (File.Exists(goodName))
					{
						File.Delete(f);
						Log.Info("Deleted by Wildcard: " + f);
						RecordInfo("Deleted duplicate: " + f);
					}
					else
					{
						File.Move(f, goodName);
						Log.Info("Renamed by Wildcard: " + f);
						RecordInfo("Renamed: " + f + " -> " + goodName);
					}
					continue;
				}
			}
			OnComplete();
		}
	}
}
