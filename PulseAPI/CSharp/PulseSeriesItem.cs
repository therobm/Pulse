namespace PulseAPI.CSharp
{
	/// <summary>
	/// Wire-type base for a single item belonging to a Series
	/// (episode or chapter). Concrete subclass sets Kind in its ctor.
	/// </summary>
	public class PulseSeriesItem : PulseObject
	{
		public string SeriesId;
		public string Title;
		public string Description;
		public string CoverArt;
		public int Duration;
		public int OrderIndex;
		public int PositionSeconds;
		public bool Completed;
	}

	/// <summary>
	/// Wire-type for a podcast episode belonging to a PulsePodcast.
	/// </summary>
	public class PulseEpisode : PulseSeriesItem
	{
		public string PublishedDate;

		public PulseEpisode()
		{
			Kind = eDataType.PodcastEpisode;
		}
	}

	/// <summary>
	/// Wire-type for an audiobook chapter belonging to a PulseAudiobook.
	/// Chapter number is OrderIndex.
	/// </summary>
	public class PulseChapter : PulseSeriesItem
	{
		public PulseChapter()
		{
			Kind = eDataType.Chapter;
		}
	}
}
