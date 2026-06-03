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
			Kind = eDataType.Artist;
		}
	}

	public class PulseArtistDetails : PulseObject
	{
		public PulseArtist Artist;
		public List<PulseAlbum> Albums;

		public PulseArtistDetails()
		{
			Albums = new List<PulseAlbum>();
			Kind = eDataType.ArtistAlbums;
		}
	}

	public class PulseArtistFullDetails : PulseObject
	{
		public PulseArtist Artist;
		public List<PulseAlbumDetails> AlbumDetails;

		public PulseArtistFullDetails()
		{
			AlbumDetails = new List<PulseAlbumDetails>();
			Kind = eDataType.ArtistTracks;
		}
	}
}
