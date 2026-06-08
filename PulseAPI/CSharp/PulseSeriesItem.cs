namespace PulseAPI.CSharp
{
	/// <summary>
	/// Wire-type base for a single item belonging to a Series
	/// (episode or chapter). Concrete subclass sets Kind in its ctor.
	/// </summary>
	public abstract class PulseSeriesItem : PulseObject
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
	public class PulsePodcastEpisode : PulseSeriesItem
	{
		public string PublishedDate;

		public PulsePodcastEpisode()
		{
			Kind = ePulseWireType.PodcastEpisode;
		}
	}

	/// <summary>
	/// Wire-type for an audiobook chapter belonging to a PulseAudiobook.
	/// Chapter number is OrderIndex.
	/// </summary>
	public class PulseChapter : PulseSeriesItem
	{
		// Chapter offsets into the underlying file, in milliseconds. EndMs == 0
		// means "the whole file" (one-file-per-chapter audiobooks). When EndMs >
		// StartMs the chapter is a window into a shared single file and the client
		// clips playback to [StartMs, EndMs).
		public int StartMs;
		public int EndMs;

		/// <summary>
		/// The id to request the audio stream with. For a chapter that is a window into
		/// a shared file (EndMs &gt; StartMs) every chapter of that file carries the SAME
		/// StreamId so the client streams/caches the file under one key. For a whole-file
		/// chapter (EndMs == 0) StreamId equals the chapter's own Id.
		/// </summary>
		public string StreamId;

		public PulseChapter()
		{
			Kind = ePulseWireType.Chapter;
		}
	}
}
