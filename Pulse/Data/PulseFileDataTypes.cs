using Pulse.MusicLibrary;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pulse.Data
{
	public class TrackRecord
	{
		public string Id { get; set; }
		public string Title { get; set; }
		public string Artist { get; set; }
		public string ArtistId { get; set; }
		public string Album { get; set; }
		public string AlbumId { get; set; }
		public string Genre { get; set; }
		public string FilePath { get; set; }
		public string CoverArtId { get; set; }
		public int TrackNumber { get; set; }
		public int DiscNumber { get; set; }
		public int Year { get; set; }
		public int DurationSeconds { get; set; }
		public long FileSizeBytes { get; set; }
		public string ContentType { get; set; }
		public string Suffix { get; set; }
		public int Rating { get; set; }
		public Dictionary<string, bool> Starred { get; set; } = new Dictionary<string, bool>();
		public DateTime LastPlayed { get; set; }
		public ScoreData Score { get; set; } = new ScoreData();
		public Dictionary<string, ScoreData> UserScore { get; set; } = new Dictionary<string, ScoreData>();

		public static TrackRecord FromTrackInfo(TrackInfo track)
		{
			TrackRecord record = new TrackRecord();
			record.Id = track.Id;
			record.Title = track.Title;
			record.Artist = track.Artist;
			record.ArtistId = track.ArtistId;
			record.Album = track.Album;
			record.AlbumId = track.AlbumId;
			record.Genre = track.Genre;
			record.FilePath = track.FilePath;
			record.CoverArtId = track.CoverArtId;
			record.TrackNumber = track.TrackNumber;
			record.DiscNumber = track.DiscNumber;
			record.Year = track.Year;
			record.DurationSeconds = track.DurationSeconds;
			record.FileSizeBytes = track.FileSizeBytes;
			record.ContentType = track.ContentType;
			record.Suffix = track.Suffix;
			record.Rating = track.Rating;
			record.Starred = track.Starred;
			record.LastPlayed = track.LastPlayed;
			record.Score = track.Score;
			record.UserScore = track.UserScore;
			return record;
		}

		public TrackInfo ToTrackInfo()
		{
			TrackInfo track = new TrackInfo();
			track.Id = Id;
			track.Title = Title;
			track.Artist = Artist;
			track.ArtistId = ArtistId;
			track.Album = Album;
			track.AlbumId = AlbumId;
			track.Genre = Genre;
			track.FilePath = FilePath;
			track.CoverArtId = CoverArtId;
			track.TrackNumber = TrackNumber;
			track.DiscNumber = DiscNumber;
			track.Year = Year;
			track.DurationSeconds = DurationSeconds;
			track.FileSizeBytes = FileSizeBytes;
			track.ContentType = ContentType;
			track.Suffix = Suffix;
			track.Rating = Rating;
			track.Starred = Starred;
			track.LastPlayed = LastPlayed;
			track.Score = Score;
			track.UserScore = UserScore;
			return track;
		}
	}

	public class AlbumRecord
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string ArtistName { get; set; }
		public string ArtistId { get; set; }
		public string Genre { get; set; }
		public string CoverArtId { get; set; }
		public int Year { get; set; }
		public Dictionary<string, bool> Starred { get; set; } = new Dictionary<string, bool>();
		public List<TrackRecord> Tracks { get; set; } = new List<TrackRecord>();

		public static AlbumRecord FromAlbumInfo(AlbumInfo album)
		{
			AlbumRecord record = new AlbumRecord();
			record.Id = album.Id;
			record.Name = album.Name;
			record.ArtistName = album.ArtistName;
			record.ArtistId = album.ArtistId;
			record.Genre = album.Genre;
			record.CoverArtId = album.CoverArtId;
			record.Year = album.Year;
			record.Starred = album.Starred;
			for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
			{
				record.Tracks.Add(TrackRecord.FromTrackInfo(album.Tracks[trackIndex]));
			}
			return record;
		}

		public AlbumInfo ToAlbumInfo()
		{
			AlbumInfo album = new AlbumInfo();
			album.Id = Id;
			album.Name = Name;
			album.ArtistName = ArtistName;
			album.ArtistId = ArtistId;
			album.Genre = Genre;
			album.CoverArtId = CoverArtId;
			album.Year = Year;
			album.Starred = Starred; 
			for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
			{
				album.Tracks.Add(Tracks[trackIndex].ToTrackInfo());
			}
			return album;
		}
	}

	public class ArtistRecord
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public Dictionary<string, bool> Starred { get; set; } = new Dictionary<string, bool>();
		public List<AlbumRecord> Albums { get; set; } = new List<AlbumRecord>();

		public static ArtistRecord FromArtistInfo(ArtistInfo artist)
		{
			ArtistRecord record = new ArtistRecord();
			record.Id = artist.Id;
			record.Name = artist.Name;
			record.Starred = artist.Starred; 
			for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
			{
				record.Albums.Add(AlbumRecord.FromAlbumInfo(artist.Albums[albumIndex]));
			}
			return record;
		}

		public ArtistInfo ToArtistInfo()
		{
			ArtistInfo artist = new ArtistInfo();
			artist.Id = Id;
			artist.Name = Name;
			artist.Starred = Starred;
			for (int albumIndex = 0; albumIndex < Albums.Count; albumIndex++)
			{
				artist.Albums.Add(Albums[albumIndex].ToAlbumInfo());
			}
			return artist;
		}
	}

	public class PlaylistRecord
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Comment { get; set; }
		public List<string> TrackIds { get; set; } = new List<string>();
		public long DurationSeconds { get; set; }

		public static PlaylistRecord FromPlaylistInfo(PlaylistInfo playlist)
		{
			PlaylistRecord record = new PlaylistRecord();
			record.Id = playlist.Id;
			record.Name = playlist.Name;
			record.Comment = playlist.Comment;
			record.TrackIds = playlist.TrackIds;
			record.DurationSeconds = playlist.DurationSeconds;
			return record;
		}

		public PlaylistInfo ToPlaylistInfo()
		{
			PlaylistInfo playlist = new PlaylistInfo();
			playlist.Id = Id;
			playlist.Name = Name;
			playlist.Comment = Comment;
			playlist.TrackIds = TrackIds;
			playlist.DurationSeconds = DurationSeconds;
			return playlist;
		}
	}

	public class PulseAnalyticsRecord
	{
		public List<string> RecentlyPlayed { get; set; } = new List<string>();
		public Dictionary<string, int> ArtistPlayCounts { get; set; } = new Dictionary<string, int>();

		public static PulseAnalyticsRecord FromInfo(PulseAnalyticsInfo info)
		{
			PulseAnalyticsRecord record = new PulseAnalyticsRecord();
			record.RecentlyPlayed = info.RecentlyPlayed;
			record.ArtistPlayCounts = info.ArtistPlayCounts;
			return record;
		}

		public PulseAnalyticsInfo ToInfo()
		{
			PulseAnalyticsInfo info = new PulseAnalyticsInfo();
			info.RecentlyPlayed = RecentlyPlayed;
			info.ArtistPlayCounts = ArtistPlayCounts;
			return info;
		}
	}
}
