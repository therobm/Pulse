using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Thump.Data;
using Thump.Utility;

namespace Thump.Pulse
{
	public class LegacyPulseObject : MediaDataObject
	{
		public string Id { get; set; }
	}

	public class LegacyPulseSearchData
	{
		public List<LegacyPulseArtist> Artists;
		public List<LegacyPulseAlbum> Albums;
		public List<LegacyPulseTrack> Tracks;
		public List<LegacyPulsePlaylist> Playlists;

		public LegacyPulseSearchData()
		{
			Artists = new List<LegacyPulseArtist>();
			Albums = new List<LegacyPulseAlbum>();
			Tracks = new List<LegacyPulseTrack>();
			Playlists = new List<LegacyPulsePlaylist>();
		}
	}


	public class LegacyPulseTrack : LegacyPulseObject
	{
		public string Title { get; set; }
		public string Artist { get; set; }
		public string ArtistId { get; set; }
		public string Album { get; set; }
		public string AlbumId { get; set; }
		public string CoverArt { private get; set; }
		public int Duration { get; set; }
		public bool Starred { get; set; }

		public LegacyPulseTrack()
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

	public class LegacyPulseAlbum : LegacyPulseObject
	{
		public string Name { get; set; }
		public string Artist { get; set; }
		public string ArtistId { get; set; }
		public string CoverArt { get; set; }
		public int Year { get; set; }
		public int TrackCount { get; set; }
		public int Duration { get; set; }
		public List<LegacyPulseTrack> Tracks { get; set; }

		public LegacyPulseAlbum()
		{
			Tracks = new List<LegacyPulseTrack>();
			Kind = eDataType.Album;
		}
	}

	public class LegacyPulsePlaylist : LegacyPulseObject
	{
		public string Name { get; set; }
		public string CoverArt { get; set; }
		public int TrackCount { get; set; }
		public int Duration { get; set; }
		public float Score { get; set; }
		public DateTime LastPlayed { get; set; }
		public List<LegacyPulseTrack> Tracks { get; set; }

		public LegacyPulsePlaylist()
		{
			Tracks = new List<LegacyPulseTrack>();
			Kind = eDataType.Playlist;
		}
	}

	public class LegacyPulseArtist : LegacyPulseObject
	{
		public string Name { get; set; }
		public string CoverArt { get; set; }
		public int AlbumCount { get; set; }
		public int PlayCount { get; set; }
		public float Score { get; set; }
		public DateTime LastPlayed { get; set; }
		public List<string> AlbumIDs { get; set; }
		public LegacyPulseArtist()
		{
			Kind = eDataType.Artist;
		}
	}

	public class LegacyPulseGenre : LegacyPulseObject
	{
		public string Name { get; set; }
		public int TrackCount { get; set; }
		public int AlbumCount { get; set; }
		public LegacyPulseGenre()
		{
			Kind = eDataType.Genre;
		}
	}

	public class LegacyPulsePodcastEpisode : LegacyPulseObject
	{
		public string Title { get; set; }
		public string Description { get; set; }
		public string StreamId { get; set; }
		public string CoverArt { get; set; }
		public string PublishDate { get; set; }
		public string Status { get; set; }
		public int Duration { get; set; }

		public LegacyPulsePodcastEpisode()
		{
			Kind = eDataType.PodcastEpisode;
		}
	}

	public class LegacyPulsePodcastChannel : LegacyPulseObject
	{
		public string Title { get; set; }
		public string Description { get; set; }
		public string CoverArt { get; set; }
		public string Url { get; set; }
		public string Status { get; set; }
		public List<LegacyPulsePodcastEpisode> Episodes { get; set; }

		public LegacyPulsePodcastChannel()
		{
			Episodes = new List<LegacyPulsePodcastEpisode>();
			Kind = eDataType.Podcast;
		}
	}

	public class LegacyPulseHelper
	{
		public static LegacyPulsePlaylist ParsePlaylist(JsonElement element)
		{
			LegacyPulsePlaylist playlist = new LegacyPulsePlaylist();
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

		public static List<LegacyPulseTrack> ParseSongArray(JsonElement parent, string propertyName)
		{
			List<LegacyPulseTrack> tracks = new List<LegacyPulseTrack>();
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

		public static LegacyPulseTrack ParseSong(JsonElement element)
		{
			LegacyPulseTrack track = new LegacyPulseTrack();
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
