using System;
using System.Collections.Generic;
using System.IO;

namespace Pulse.DataStorage
{
	public enum eRetentionPolicy
	{
		KeepAll,
		KeepN,
		KeepDays,
		KeepExisting,
	}

	public enum eDownloadState
	{
		Discovered,
		Downloading,
		Downloaded,
		Failed,
	}

	public class Podcast : PulseDataObject
	{
		public class UserData
		{
			public bool Subscribed;
			public string LastEpisodeId = "";
			public DateTime LastPlayed;
		}

		public string Title = "";
		public string Author = "";
		public string Description = "";
		public string ArtworkPath = "";
		public string DateAdded = "";
		public Dictionary<string, UserData> Users = new Dictionary<string, UserData>();

		private string m_feedUrl = "";
		public string FeedUrl
		{
			get { return m_feedUrl; }
			set { m_feedUrl = value; m_bIsDirty = true; }
		}

		private eRetentionPolicy m_retention = eRetentionPolicy.KeepAll;
		public eRetentionPolicy Retention
		{
			get { return m_retention; }
			set { m_retention = value; m_bIsDirty = true; }
		}

		private int m_retentionValue;
		public int RetentionValue
		{
			get { return m_retentionValue; }
			set { m_retentionValue = value; m_bIsDirty = true; }
		}

		private bool m_autoDownload;
		public bool AutoDownload
		{
			get { return m_autoDownload; }
			set { m_autoDownload = value; m_bIsDirty = true; }
		}
	}

	public class Episode : PulseDataObject
	{
		public class UserData
		{
			public int PositionSeconds;
			public bool Completed;
			public DateTime LastPlayed;
		}

		public string PodcastId = "";
		public string Guid = "";
		public string Title = "";
		public string Description = "";
		public int OrderIndex;
		public string PublishedDate = "";
		public string MediaSourceUrl = "";
		public int StartMs;
		public int EndMs;
		public Dictionary<string, UserData> Users = new Dictionary<string, UserData>();

		private int m_durationSeconds;
		public int DurationSeconds
		{
			get { return m_durationSeconds; }
			set { m_durationSeconds = value; m_bIsDirty = true; }
		}

		private string m_localPath = "";
		public string LocalPath
		{
			get { return m_localPath; }
			set { m_localPath = value; m_bIsDirty = true; }
		}

		private long m_fileSizeBytes;
		public long FileSizeBytes
		{
			get { return m_fileSizeBytes; }
			set { m_fileSizeBytes = value; m_bIsDirty = true; }
		}

		private eDownloadState m_downloadState = eDownloadState.Discovered;
		public eDownloadState DownloadState
		{
			get { return m_downloadState; }
			set { m_downloadState = value; m_bIsDirty = true; }
		}

		public bool NeedsDownload()
		{
			if (DownloadState == eDownloadState.Downloading)
			{
				return false;
			}
			if (DownloadState != eDownloadState.Downloaded)
			{
				return true;
			}
			if (string.IsNullOrEmpty(LocalPath) || !File.Exists(LocalPath))
			{
				return true;
			}
			return false;
		}
	}
}
