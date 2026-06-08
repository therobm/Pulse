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

	public class PodcastData : PulseDataObject
	{
		public string Title { get; set; } = "";
		public string Author { get; set; } = "";
		public string Description { get; set; } = "";
		public string ArtworkPath { get; set; } = "";
		public string FeedUrl { get; set; } = "";
		public DateTime DateAdded { get; set; }
		public eRetentionPolicy Retention { get; set; } = eRetentionPolicy.KeepAll;
		public int RetentionValue { get; set; }
		public bool AutoDownload { get; set; }
		public Dictionary<string, PodcastUserState> UserState { get; set; } = new Dictionary<string, PodcastUserState>();
	}

	public class PodcastUserState
	{
		public bool Subscribed { get; set; }
		public string LastEpisodeId { get; set; } = "";
		public DateTime LastPlayed { get; set; }
	}

	public class PodcastEpisodeData : PulseDataObject
	{
		public string PodcastId { get; set; } = "";
		public string Guid { get; set; } = "";
		public string Title { get; set; } = "";
		public string Description { get; set; } = "";
		public int DurationSeconds { get; set; }
		public int OrderIndex { get; set; }
		public DateTime PublishedDate { get; set; }
		public string MediaSourceUrl { get; set; } = "";
		public string LocalPath { get; set; } = "";
		public long FileSizeBytes { get; set; }
		public eDownloadState DownloadState { get; set; } = eDownloadState.Discovered;
		public Dictionary<string, EpisodeUserState> UserState { get; set; } = new Dictionary<string, EpisodeUserState>();
	}

	public class EpisodeUserState
	{
		public int PositionSeconds { get; set; }
		public bool Completed { get; set; }
		public DateTime LastPlayed { get; set; }
	}
}
