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
	/// One series row: the parent of an ordered set of audio items. Plain
	/// public-field data bag; SeriesDB reads/writes columns directly. Strings
	/// default to "" so absent fields don't round-trip as NULL.
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
	}
}
