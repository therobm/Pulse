namespace Pulse.Series
{
	/// <summary>
	/// Which family a Series belongs to. Stored as the enum member name in the
	/// series.series_type column (e.g. "Podcast"). Both kinds share the same
	/// row shape: an ordered set of audio items with per-user progress; the
	/// type just decides which features (RSS polling for podcasts, retention
	/// rules) apply on top.
	/// </summary>
	public enum eSeriesType
	{
		Podcast,
		Audiobook
	}

	/// <summary>
	/// How a podcast feed culls already-downloaded items as new ones arrive.
	/// Persisted as the enum member name in series.retention_policy. KeepAll
	/// ignores RetentionValue; KeepN keeps the newest N items; KeepDays keeps
	/// items whose published date is within the last N days.
	/// </summary>
	public enum eRetentionPolicy
	{
		KeepAll,
		KeepN,
		KeepDays
	}

	/// <summary>
	/// One series row: the parent of an ordered set of audio items. Plain
	/// public-field data bag; SeriesDB reads/writes columns directly. Strings
	/// default to "" so absent fields don't round-trip as NULL. The feed-*
	/// fields and Retention/AutoDownload are populated for podcasts and sit
	/// at defaults for audiobooks (consistent with Narrator/Collection only
	/// being meaningful on the audiobook side).
	/// </summary>
	public class SeriesInfo
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
		public string LastPolled = "";
		public int PollIntervalMinutes = 60;
		public eRetentionPolicy Retention = eRetentionPolicy.KeepAll;
		public int RetentionValue = 0;
		public bool AutoDownload = false;
	}
}
