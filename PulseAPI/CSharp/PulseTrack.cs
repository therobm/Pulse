namespace PulseAPI.CSharp
{
	/// <summary>
	/// Which kind of series a track belongs to, when IsSeries is true. Drives
	/// which server progress endpoint the client saves resume position to. None
	/// for ordinary music tracks.
	/// </summary>
	public enum ePulseSeriesKind
	{
		None = 0,
		Podcast = 1,
		Audiobook = 2,
	}

	public class PulseTrack : PulseMusicObject
	{
		public string Title;
		public string Artist;
		public string ArtistId;
		public string Album;
		public string AlbumId;
		public string CoverArt;
		public int Duration;
		public bool Starred;
		public bool IsSeries;

		/// <summary>
		/// For series content (podcast episode / audiobook chapter), which kind it
		/// is, so the client routes saved progress to the right endpoint. None for
		/// ordinary tracks.
		/// </summary>
		public ePulseSeriesKind SeriesKind;

		/// <summary>
		/// Saved resume position in seconds for this series item, relative to the
		/// item's own start (i.e. relative to a chapter's clip window). 0 means
		/// start from the beginning. Populated client-side from the episode/chapter
		/// payload; ignored for ordinary tracks.
		/// </summary>
		public int ResumePositionSeconds;

		/// <summary>
		/// Audiobook-chapter clip window start in milliseconds. 0 for ordinary
		/// tracks (no clipping).
		/// </summary>
		public int StartMs;

		/// <summary>
		/// Audiobook-chapter clip window end in milliseconds. 0 for ordinary
		/// tracks (no clipping); when greater than StartMs the client clips
		/// playback to [StartMs, EndMs).
		/// </summary>
		public int EndMs;

		/// <summary>
		/// Shared stream id for an audiobook chapter that is a window into a
		/// single file: every chapter of that file carries the same StreamId so
		/// the file streams/caches under one key. Null or empty for ordinary
		/// tracks, which stream by Id.
		/// </summary>
		public string StreamId;

		public PulseTrack()
		{
			Kind = ePulseWireType.Track;
		}

		public string GetImageId()
		{
			if (!string.IsNullOrEmpty(CoverArt))
			{
				return CoverArt;
			}
			if (!string.IsNullOrEmpty(AlbumId))
			{
				return AlbumId;
			}
			return null;
		}
	}
}
