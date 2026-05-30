using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Thump.Data;
using Thump.Utility;

namespace Thump.Pulse
{
	public class PulseObject : ThumpDataOb
	{
		public string Id { get; set; }
	}

	public class PulseSearchData
	{
		public List<PulseArtist> Artists;
		public List<PulseAlbum> Albums;
		public List<PulseTrack> Songs;
		public List<PulsePlaylist> Playlists;

		public PulseSearchData()
		{
			Artists = new List<PulseArtist>();
			Albums = new List<PulseAlbum>();
			Songs = new List<PulseTrack>();
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
		public int SongCount { get; set; }
		public int Duration { get; set; }
		public List<PulseTrack> Songs { get; set; }

		public PulseAlbum()
		{
			Songs = new List<PulseTrack>();
			Kind = eDataType.Album;
		}
	}

	public class PulsePlaylist : PulseObject
	{
		public string Name { get; set; }
		public string CoverArt { get; set; }
		public int SongCount { get; set; }
		public int Duration { get; set; }
		public float Score { get; set; }
		public DateTime LastPlayed { get; set; }
		public List<PulseTrack> Songs { get; set; }

		public PulsePlaylist()
		{
			Songs = new List<PulseTrack>();
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

		public PulseArtist()
		{
			Kind = eDataType.Artist;
		}
	}

	public class PulseGenre : PulseObject
	{
		public string Name { get; set; }
		public int SongCount { get; set; }
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

	public class PulseHelper
	{
		public static PulsePlaylist ParsePlaylist(JsonElement element)
		{
			PulsePlaylist playlist = new PulsePlaylist();
			playlist.Id = JsonHelper.GetString(element, "id");
			playlist.Name = JsonHelper.GetString(element, "name");
			playlist.CoverArt = JsonHelper.GetString(element, "coverArt");
			playlist.SongCount = JsonHelper.GetInt(element, "songCount");
			playlist.Duration = JsonHelper.GetInt(element, "duration");
			playlist.Score = JsonHelper.GetFloat(element, "score");
			playlist.LastPlayed = JsonHelper.GetDateTime(element, "lastPlayed");
			playlist.Songs = ParseSongArray(element, "entry");
			return playlist;
		}

		public static List<PulseTrack> ParseSongArray(JsonElement parent, string propertyName)
		{
			List<PulseTrack> songs = new List<PulseTrack>();
			JsonElement array;
			bool validParams = true;
			if (!parent.TryGetProperty(propertyName, out array))
				validParams = false;
			if (array.ValueKind != JsonValueKind.Array)
				validParams = false;
			if (validParams)
			{
				foreach (JsonElement element in array.EnumerateArray())
				{
					songs.Add(ParseSong(element));
				}
			}
			return songs;
		}

		public static PulseTrack ParseSong(JsonElement element)
		{
			PulseTrack song = new PulseTrack();
			song.Id = JsonHelper.GetString(element, "id");
			song.Title = JsonHelper.GetString(element, "title");
			song.Artist = JsonHelper.GetString(element, "artist");
			song.ArtistId = JsonHelper.GetString(element, "artistId");
			song.Album = JsonHelper.GetString(element, "album");
			song.AlbumId = JsonHelper.GetString(element, "albumId");
			song.CoverArt = JsonHelper.GetString(element, "coverArt");
			song.Duration = JsonHelper.GetInt(element, "duration");
			string starredValue = JsonHelper.GetString(element, "starred");
			song.Starred = !string.IsNullOrEmpty(starredValue);
			return song;
		}
	}
}
