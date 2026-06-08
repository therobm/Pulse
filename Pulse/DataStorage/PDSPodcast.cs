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
		public string FeedUrl = "";
		public eRetentionPolicy Retention = eRetentionPolicy.KeepAll;
		public int RetentionValue;
		public bool AutoDownload;
		public Dictionary<string, UserData> Users = new Dictionary<string, UserData>();
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
		public int DurationSeconds;
		public int OrderIndex;
		public string PublishedDate = "";
		public string MediaSourceUrl = "";
		public string LocalPath = "";
		public long FileSizeBytes;
		public eDownloadState DownloadState = eDownloadState.Discovered;
		public int StartMs;
		public int EndMs;
		public Dictionary<string, UserData> Users = new Dictionary<string, UserData>();

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
