using Pulse.MusicLibrary;
using System;
using System.Collections.Generic;

namespace Pulse.Protocols
{
	// Wire types for the Pulse-native API responses. Public fields, lowercase
	// names to match the JSON exactly -- no name transformations, no Json
	// attributes.

	public class PulseAPI_Ping
	{
		public bool ok = true;
		public string serverVersion = "";
	}

	public class PulseAPI_OkResult
	{
		public bool ok = true;
	}

	public class PulseAPI_Error
	{
		public string error = "";
	}

	public class PulseAPI_Genre
	{
		public string name = "";
		public int songCount;
		public int albumCount;
	}

	// Full track info, used for /pulse/track, /pulse/tracks, and as the tracks
	// entries inside album and playlist responses.
	public class PulseAPI_Track
	{
		public string id = "";
		public string title = "";
		public string artist = "";
		public string artistId = "";
		public string album = "";
		public string albumId = "";
		public int duration;
		public string coverArt = "";
		public int trackNumber;
		public int discNumber;
		public int year;

		public PulseAPI_Track(TrackInfo track)
		{
			id = track.Id;
			title = track.Title;
			artist = track.Artist;
			artistId = track.ArtistId;
			album = track.Album;
			albumId = track.AlbumId;
			duration = track.DurationSeconds;
			coverArt = track.CoverArtId;
			trackNumber = track.TrackNumber;
			discNumber = track.DiscNumber;
			year = track.Year;
		}
	}

	// Album entry as it appears nested under an artist's albums list.
	public class PulseAPI_AlbumSummary
	{
		public string id = "";
		public string name = "";
		public int year;
		public string coverArt = "";
	}

	// Full album response (/pulse/album).
	public class PulseAPI_Album
	{
		public string id = "";
		public string name = "";
		public string artistId = "";
		public string artistName = "";
		public int year;
		public string coverArt = "";
		public List<PulseAPI_Track> tracks = new List<PulseAPI_Track>();
	}

	// Artist entry in /pulse/artists.
	public class PulseAPI_ArtistSummary
	{
		public string id = "";
		public string name = "";
		public int albumCount;
		public string coverArt = "";
	}

	// Full artist response (/pulse/artist).
	public class PulseAPI_Artist
	{
		public string id = "";
		public string name = "";
		public string coverArt = "";
		public int albumCount;
		public List<PulseAPI_AlbumSummary> albums = new List<PulseAPI_AlbumSummary>();
	}

	// Playlist entry in /pulse/playlists.
	public class PulseAPI_PlaylistSummary
	{
		public string id = "";
		public string name = "";
		public string comment = "";
		public int songCount;
		public long duration;
		public string coverArt = "";
		public DateTime lastPlayed;
	}

	// Full playlist response (/pulse/playlist).
	public class PulseAPI_Playlist
	{
		public string id = "";
		public string name = "";
		public string comment = "";
		public int songCount;
		public long duration;
		public string coverArt = "";
		public DateTime lastPlayed;
		public List<PulseAPI_Track> tracks = new List<PulseAPI_Track>();
	}
}
