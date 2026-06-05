namespace Pulse.Series
{
	/// <summary>
	/// Where an item is in the discover -> download lifecycle. Stored as the
	/// enum member name in series_items.download_state. Discovered means the
	/// item is known (from RSS or a manifest) but not yet pulled to disk;
	/// Downloading is a transient state held during the network fetch;
	/// Downloaded means LocalPath points at a playable file; Failed records a
	/// terminal failure so the puller doesn't retry endlessly.
	/// </summary>
	public enum eDownloadState
	{
		Discovered,
		Downloading,
		Downloaded,
		Failed
	}

	/// <summary>
	/// One item inside a Series: a podcast episode or an audiobook chapter.
	/// Ordering inside a series is given by OrderIndex (lower = earlier).
	/// LocalPath is empty until the item is downloaded; MediaSourceUrl is the
	/// remote/source URL the item was discovered at.
	/// </summary>
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
	}
}
