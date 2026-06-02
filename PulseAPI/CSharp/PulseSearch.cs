using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	public class PulseSearchData
	{
		public List<PulseArtist> Artists;
		public List<PulseAlbum> Albums;
		public List<PulseTrack> Tracks;
		public List<PulsePlaylist> Playlists;

		public PulseSearchData()
		{
			Artists = new List<PulseArtist>();
			Albums = new List<PulseAlbum>();
			Tracks = new List<PulseTrack>();
			Playlists = new List<PulsePlaylist>();
		}
	}
}
