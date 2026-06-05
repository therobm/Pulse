namespace Pulse.Series
{
	/// <summary>
	/// How a podcast feed culls already-downloaded items as new ones arrive.
	/// Persisted as the enum member name in podcast_feeds.retention_policy.
	/// KeepAll ignores RetentionValue; KeepN keeps the newest N items;
	/// KeepDays keeps items whose published date is within the last N days.
	/// </summary>
	public enum eRetentionPolicy
	{
		KeepAll,
		KeepN,
		KeepDays
	}

	/// <summary>
	/// Podcast-specific extension row for a Series whose Type is Podcast.
	/// One-to-one with the parent series via SeriesId. Audiobooks have no
	/// corresponding row in podcast_feeds.
	/// </summary>
	public class PodcastFeedInfo
	{
		public string SeriesId = "";
		public string FeedUrl = "";
		public string LastPolled = "";
		public int PollIntervalMinutes = 60;
		public eRetentionPolicy Retention = eRetentionPolicy.KeepAll;
		public int RetentionValue = 0;
		public bool AutoDownload = false;
	}
}
