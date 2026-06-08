using System.IO;

namespace Pulse.Series
{
	public enum eDownloadState
	{
		Discovered,
		Downloading,
		Downloaded,
		Failed
	}

	public enum eSeriesType
	{
		Podcast,
		Audiobook
	}

	public enum eRetentionPolicy
	{
		KeepAll,
		KeepN,
		KeepDays,
		KeepExisting, //no new downloads but don't evict old ones
	}

	public class SeriesTypes
	{
		public string Id = "";
		public eSeriesType Type = eSeriesType.Podcast;
		public string Title = "";
		public string Author = "";
		public string Description = "";
		public string ArtworkPath = "";
		public string DateAdded = "";
		public string Narrator = "";
		public string Collection = "";
		public int CollectionIndex = 0;
		public string FeedUrl = "";
		public eRetentionPolicy Retention = eRetentionPolicy.KeepAll;
		public int RetentionValue = 0;
		public bool AutoDownload = false;
	}

	public class SeriesUserDataInfo
	{
		public string SeriesId = "";
		public string UserName = "";
		public bool Subscribed = false;
		public string LastItemId = "";
		public string LastPlayed = "";
		public string DateAdded = "";
	}

	public class SeriesItemInfo
	{
		public string Id = "";
		public string SeriesId = "";
		public string Guid = "";
		public string Title = "";
		public string Description = "";
		public int DurationSeconds = 0;
		public int OrderIndex = 0;
		public string PublishedDate = "";
		public string MediaSourceUrl = "";
		public string LocalPath = "";
		public long FileSizeBytes = 0;
		public eDownloadState DownloadState = eDownloadState.Discovered;
		// Chapter offsets into LocalPath, in milliseconds. EndMs == 0 means "play
		// the whole file" (podcast episodes, one-file-per-chapter audiobooks). When
		// EndMs > StartMs the item is a time window into a shared file (single-file
		// audiobook with embedded chapters).
		public int StartMs = 0;
		public int EndMs = 0;

		public bool NeedsDownload()
		{
			if (DownloadState == eDownloadState.Downloading)
				return false;

			if (DownloadState != eDownloadState.Downloaded)
				return true;

			//if our on disk is missing, we want to download again
			if (string.IsNullOrEmpty(LocalPath) || !File.Exists(LocalPath))
				return true;

			return false;
		}
	}

	public class SeriesItemUserDataInfo
	{
		public string ItemId = "";
		public string UserName = "";
		public int PositionSeconds = 0;
		public bool Completed = false;
		public string LastPlayed = "";
	}
}
