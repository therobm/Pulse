using System;
using System.Collections.Generic;

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

	public class PodcastSeriesData : PulseDataObject
	{
		public class UserState
		{
			public bool Subscribed;
			public string LastEpisodeId = "";
			public DateTime LastPlayed;
		}

		public string Title = "";
		public string Author = "";
		public string Description = "";
		public string ArtworkPath = "";
		public string FeedUrl = "";
		public eRetentionPolicy Retention = eRetentionPolicy.KeepN;
		public int RetentionValue;
		public bool AutoDownload;
		public Dictionary<string, UserState> Users = new Dictionary<string, UserState>();
	}

	public class PodcastEpisodeData : PulseDataObject
	{
		public class UserState
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
		public DateTime PublishedDate;
		public string MediaSourceUrl = "";
		public string LocalPath = "";
		public long FileSizeBytes;
		public eDownloadState DownloadState = eDownloadState.Discovered;
		public Dictionary<string, UserState> Users = new Dictionary<string, UserState>();
	}
}
