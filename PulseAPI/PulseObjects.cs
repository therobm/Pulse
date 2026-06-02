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
		public eDataType Kind { get; set; }
	}

	public class PulseObject : MediaDataObject
	{
		public string Id { get; set; }
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
		public string Title { get; set; }
		public string Artist { get; set; }
		public string ArtistId { get; set; }
		public string Album { get; set; }
		public string AlbumId { get; set; }
		public string CoverArt { private get; set; }
		public int Duration { get; set; }
		public bool Starred { get; set; }

		public PulseTrack()
		{
			Kind = eDataType.Track;
		}

		public string ImageID
		{
			get
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
	}

	public class PulseAlbum : PulseObject
	{
		public string Name { get; set; }
		public string Artist { get; set; }
		public string ArtistId { get; set; }
		public string CoverArt { get; set; }
		public int Year { get; set; }
		public int TrackCount { get; set; }
		public int Duration { get; set; }
		public List<PulseTrack> Tracks { get; set; }

		public PulseAlbum()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.Album;
		}
	}

	public class PulsePlaylist : PulseObject
	{
		public string Name { get; set; }
		public string CoverArt { get; set; }
		public int TrackCount { get; set; }
		public int Duration { get; set; }
		public float Score { get; set; }
		public DateTime LastPlayed { get; set; }
		public List<PulseTrack> Tracks { get; set; }

		public PulsePlaylist()
		{
			Tracks = new List<PulseTrack>();
			Kind = eDataType.Playlist;
		}
	}

	public class PulseArtist : PulseObject
	{
		public string Name { get; set; }
		public string CoverArt { get; set; }
		public int AlbumCount { get; set; }
		public int PlayCount { get; set; }
		public float Score { get; set; }
		public DateTime LastPlayed { get; set; }
		public List<string> AlbumIDs { get; set; }
		public PulseArtist()
		{
			Kind = eDataType.Artist;
		}
	}

	public class PulseGenre : PulseObject
	{
		public string Name { get; set; }
		public int TrackCount { get; set; }
		public int AlbumCount { get; set; }
		public PulseGenre()
		{
			Kind = eDataType.Genre;
		}
	}

	public class PulsePodcastEpisode : PulseObject
	{
		public string Title { get; set; }
		public string Description { get; set; }
		public string StreamId { get; set; }
		public string CoverArt { get; set; }
		public string PublishDate { get; set; }
		public string Status { get; set; }
		public int Duration { get; set; }

		public PulsePodcastEpisode()
		{
			Kind = eDataType.PodcastEpisode;
		}
	}

	public class PulsePodcastChannel : PulseObject
	{
		public string Title { get; set; }
		public string Description { get; set; }
		public string CoverArt { get; set; }
		public string Url { get; set; }
		public string Status { get; set; }
		public List<PulsePodcastEpisode> Episodes { get; set; }

		public PulsePodcastChannel()
		{
			Episodes = new List<PulsePodcastEpisode>();
			Kind = eDataType.Podcast;
		}
	}
}
