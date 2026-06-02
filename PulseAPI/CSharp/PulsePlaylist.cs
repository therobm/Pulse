using System;
using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	public class PulsePlaylist : PulseObject
	{
		public string Name;
		public string CoverArt;
		public int TrackCount;
		public int Duration;
		public float Score;
		public DateTime LastPlayed;

		public PulsePlaylist()
		{
			Kind = eDataType.Playlist;
		}
	}

	public class PulsePlaylistDetails : PulseObject
	{
		public PulsePlaylist Playlist;
		public List<PulseTrack> Tracks;

		public PulsePlaylistDetails()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.PlaylistTracks;
		}
	}
}
