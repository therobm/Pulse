using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	public class PulseGenre : PulseObject
	{
		public string Name;
		public int TrackCount;
		public int AlbumCount;

		public PulseGenre()
		{
			Kind = ePulseWireType.Genre;
		}
	}

	public class PulseGenreDetails : PulseObject
	{
		public PulseGenre Genre;
		public List<PulseTrack> Tracks;

		public PulseGenreDetails()
		{
			Tracks = new List<PulseTrack>();
			Kind = ePulseWireType.GenreDetails;
		}
	}
}
