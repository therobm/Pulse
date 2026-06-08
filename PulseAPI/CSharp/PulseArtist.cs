using System;
using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	public class PulseArtist : PulseObject
	{
		public string Name;
		public string CoverArt;
		public int AlbumCount;
		public int TrackCount;
		public float Score;
		public DateTime LastPlayed;

		public PulseArtist()
		{
			Kind = ePulseWireType.Artist;
		}
	}

	public class PulseArtistDetails : PulseObject
	{
		public PulseArtist Artist;
		public List<PulseAlbum> Albums;

		public PulseArtistDetails()
		{
			Albums = new List<PulseAlbum>();
			Kind = ePulseWireType.ArtistAlbums;
		}
	}

	public class PulseArtistFullDetails : PulseObject
	{
		public PulseArtist Artist;
		public List<PulseAlbumDetails> AlbumDetails;

		public PulseArtistFullDetails()
		{
			AlbumDetails = new List<PulseAlbumDetails>();
			Kind = ePulseWireType.ArtistTracks;
		}
	}
}
