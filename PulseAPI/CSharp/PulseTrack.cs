namespace PulseAPI.CSharp
{
	public class PulseTrack : PulseObject
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
			Kind = eDataType.Track;
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
