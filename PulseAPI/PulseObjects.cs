using System;
using System.Collections.Generic;

namespace PulseAPI
{
	/// <summary>
	/// 1:1 mapping of PulseObject type
	/// useful for avoiding string comparisons and reflection
	/// </summary>
	public enum eDataType
	{
		Track,
		Album,
		AlbumTracks,
		Playlist,
		PlaylistTracks,
		Artist,
		ArtistAlbums,
		ArtistTracks,
		Genre,
		GenreTracks,
		Podcast,
		PodcastEpisodes,
		PodcastEpisode,
		CoverArt,
		SongData
	}

	public class PulseObject
	{
		public string Id;
		public eDataType Kind;
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

		public PulseAlbum()
		{
			Kind = eDataType.Album;
		}
	}

	public class PulseAlbumDetail : PulseObject
	{
		public PulseAlbum Album;
		public List<PulseTrack> Tracks;

		public PulseAlbumDetail()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.AlbumTracks;
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

		public PulsePlaylist()
		{
			Kind = eDataType.Playlist;
		}
	}

	public class PulsePlaylistDetail : PulseObject
	{
		public PulsePlaylist Playlist;
		public List<PulseTrack> Tracks;

		public PulsePlaylistDetail()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.PlaylistTracks;
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

	public class PulseArtistAlbums : PulseObject
	{
		public List<PulseAlbum> Albums;

		public PulseArtistAlbums()
		{
			Albums = new List<PulseAlbum>();
			Kind = eDataType.ArtistAlbums;
		}
	}

	public class PulseArtistTracks : PulseObject
	{
		public List<PulseTrack> Tracks;

		public PulseArtistTracks()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.ArtistTracks;
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

	public class PulseGenreTracks : PulseObject
	{
		public List<PulseTrack> Tracks;

		public PulseGenreTracks()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.GenreTracks;
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

		public PulsePodcastChannel()
		{
			Kind = eDataType.Podcast;
		}
	}

	public class PulsePodcastEpisodes : PulseObject
	{
		public List<PulsePodcastEpisode> Episodes;

		public PulsePodcastEpisodes()
		{
			Episodes = new List<PulsePodcastEpisode>();
			Kind = eDataType.PodcastEpisodes;
		}
	}
}
