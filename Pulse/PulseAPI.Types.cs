using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Thump.Data;
using Thump.Utility;

namespace Thump.Pulse
{
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

	public class PulseHelper
	{
		public static PulsePlaylist ParsePlaylist(JsonElement element)
		{
			PulsePlaylist playlist = new PulsePlaylist();
			playlist.Id = JsonHelper.GetString(element, "id");
			playlist.Name = JsonHelper.GetString(element, "name");
			playlist.CoverArt = JsonHelper.GetString(element, "coverArt");
			playlist.TrackCount = JsonHelper.GetInt(element, "songCount");
			playlist.Duration = JsonHelper.GetInt(element, "duration");
			playlist.Score = JsonHelper.GetFloat(element, "score");
			playlist.LastPlayed = JsonHelper.GetDateTime(element, "lastPlayed");
			playlist.Tracks = ParseSongArray(element, "entry");
			return playlist;
		}

		public static List<PulseTrack> ParseSongArray(JsonElement parent, string propertyName)
		{
			List<PulseTrack> tracks = new List<PulseTrack>();
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
					tracks.Add(ParseSong(element));
				}
			}
			return tracks;
		}

		public static PulseTrack ParseSong(JsonElement element)
		{
			PulseTrack track = new PulseTrack();
			track.Id = JsonHelper.GetString(element, "id");
			track.Title = JsonHelper.GetString(element, "title");
			track.Artist = JsonHelper.GetString(element, "artist");
			track.ArtistId = JsonHelper.GetString(element, "artistId");
			track.Album = JsonHelper.GetString(element, "album");
			track.AlbumId = JsonHelper.GetString(element, "albumId");
			track.CoverArt = JsonHelper.GetString(element, "coverArt");
			track.Duration = JsonHelper.GetInt(element, "duration");
			string starredValue = JsonHelper.GetString(element, "starred");
			track.Starred = !string.IsNullOrEmpty(starredValue);
			return track;
		}
	}
}
