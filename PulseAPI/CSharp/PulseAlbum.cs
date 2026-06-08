using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	public class PulseAlbum : PulseObject
	{
		public string Name;
		public string Artist;
		public string ArtistId;
		public string CoverArt;
		public int Year;
		public int TrackCount;
		public int Duration;

		public PulseAlbum()
		{
			Kind = ePulseWireType.Album;
		}
	}

	public class PulseAlbumDetails : PulseObject
	{
		public PulseAlbum Album;
		public List<PulseTrack> Tracks;

		public PulseAlbumDetails()
		{
			Tracks = new List<PulseTrack>();
			Kind = ePulseWireType.AlbumTracks;
		}
	}
}
