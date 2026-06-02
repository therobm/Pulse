using System;
using System.Collections.Generic;

namespace PulseAPI
{
	public enum eDataType
	{
		Track,
		Album,
		Playlist,
		Artist,
		CoverArt,
		SongData,
		Genre,
		Podcast,
		PodcastEpisode
	}

	public class MediaDataObject
	{
		public eDataType Kind;
	}

	public class PulseObject : MediaDataObject
	{
		public string Id;
	}

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

	public class PulseAlbum : PulseObject
	{
		public string Name;
		public string Artist;
		public string ArtistId;
		public string CoverArt;
		public int Year;
		public int TrackCount;
		public int Duration;
		public List<PulseTrack> Tracks;

		public PulseAlbum()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.Album;
		}
	}

	public class PulsePlaylist : PulseObject
	{
		public string Name;
		public string CoverArt;
		public int TrackCount;
		public int Duration;
		public float Score;
		public DateTime LastPlayed;
		public List<PulseTrack> Tracks;

		public PulsePlaylist()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.Playlist;
		}
	}

	public class PulseArtist : PulseObject
	{
		public string Name;
		public string CoverArt;
		public int AlbumCount;
		public int PlayCount;
		public float Score;
		public DateTime LastPlayed;
		public List<string> AlbumIds;

		public PulseArtist()
		{
			Kind = eDataType.Artist;
		}
	}

	public class PulseGenre : PulseObject
	{
		public string Name;
		public int TrackCount;
		public int AlbumCount;

		public PulseGenre()
		{
			Kind = eDataType.Genre;
		}
	}

	public class PulsePodcastEpisode : PulseObject
	{
		public string Title;
		public string Description;
		public string StreamId;
		public string CoverArt;
		public string PublishDate;
		public string Status;
		public int Duration;

		public PulsePodcastEpisode()
		{
			Kind = eDataType.PodcastEpisode;
		}
	}

	public class PulsePodcastChannel : PulseObject
	{
		public string Title;
		public string Description;
		public string CoverArt;
		public string Url;
		public string Status;
		public List<PulsePodcastEpisode> Episodes;

		public PulsePodcastChannel()
		{
			Episodes = new List<PulsePodcastEpisode>();
			Kind = eDataType.Podcast;
		}
	}
}
