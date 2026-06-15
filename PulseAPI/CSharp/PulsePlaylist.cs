using System;
using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	public class PulsePlaylist : PulseMusicObject
	{
		public string Name;
		public string Comment;
		public string CoverArt;
		public int TrackCount;
		[Obsolete("just no this is silly")]
		public int Duration;

		public PulsePlaylist()
		{
			Kind = ePulseWireType.Playlist;
		}
	}

	public class PulsePlaylistDetails : PulseObject
	{
		public PulsePlaylist Playlist;
		public List<PulseTrack> Tracks;

		public PulsePlaylistDetails()
		{
			Tracks = new List<PulseTrack>();
			Kind = ePulseWireType.PlaylistTracks;
		}
	}
}
