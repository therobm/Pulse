namespace PulseAPI.CSharp
{
	/// <summary>
	/// Wire-type base for a Series (podcast or audiobook). Shared
	/// metadata only; the concrete subclass sets Kind in its ctor.
	/// </summary>
	public class PulseSeries : PulseObject
	{
		public string Title;
		public string Author;
		public string Narrator;
		public string Description;
		public string CoverArt;
		public string Collection;
		public int CollectionIndex;
		public bool Subscribed;
		public int ItemCount;
		public int UnplayedCount;
		public string LastItemId;
		public string LastPlayed;
	}

	/// <summary>
	/// Wire-type for a podcast series.
	/// </summary>
	public class PulsePodcast : PulseSeries
	{
		public int EpisodeCount;
		public bool AutoDownload;
		public string FeedUrl;

		public PulsePodcast()
		{
			Kind = eDataType.Podcast;
		}
	}

	/// <summary>
	/// Wire-type for an audiobook series. TotalDuration is the sum
	/// of chapter durations in seconds.
	/// </summary>
	public class PulseAudiobook : PulseSeries
	{
		public int TotalDuration;

		public PulseAudiobook()
		{
			Kind = eDataType.Audiobook;
		}
	}
}
