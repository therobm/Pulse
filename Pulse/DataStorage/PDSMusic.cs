using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;


namespace Pulse.DataStorage
{
	public enum eDataType
	{
		Track,
		Album,
		Artist,
		Playlist,
		PulseAnalytics,
		Genre,
		User,
		Podcast,
		PodcastEpisode,
		Audiobook,
		AudiobookChapter,
		Diagnostic,
		AnalyticRecordItem,
	}

	public abstract class PulseDataObject
	{
		public string Id;

		[JsonIgnore]
		private bool m_bIsDirty;
		public void ClearDirty()
		{
			m_bIsDirty = false;
		}
		public bool IsDirty()
		{
			return m_bIsDirty;
		}
		public void MarkDirty()
		{
			m_bIsDirty = true;
		}

	}


	public class TrackData : PulseDataObject
	{
		public string LegacyId;
		public string Title;
		public string Artist;
		public string ArtistId;
		public string Album;
		public string AlbumId;
		public string Genre;
		[Obsolete("Replaced by RelativeFilePath")]
		public string FilePath;
		public string RelativeFilePath;
		public string CoverArtId;
		public int TrackNumber;
		public int DiscNumber;
		public int Year;
		public int DurationSeconds;
		public long FileSizeBytes;
		public string ContentType;
		public string Suffix;

	
		public DateTime LastPlayed;

		[JsonIgnore]
		public ArtistData ParentArtist;

		
		public PulseTrack BuildPulse()
		{
			PulseTrack pulseTrack = new PulseTrack();
			pulseTrack.Id = Id;
			pulseTrack.Title = Title;
			pulseTrack.Artist = Artist;
			pulseTrack.ArtistId = ArtistId;
			pulseTrack.Album = Album;
			pulseTrack.AlbumId = AlbumId;
			pulseTrack.CoverArt = CoverArtId;
			pulseTrack.Duration = DurationSeconds;
			return pulseTrack;
		}
	}

	
	public class AlbumData : PulseDataObject
	{
		public string Name;
		public string ArtistName;
		public string ArtistId;
		public string Genre;
		public string CoverArtId;
		public int Year;

		[JsonIgnore]
		public List<TrackData> Tracks = new List<TrackData>();

		public PulseAlbum BuildPulse()
		{
			PulseAlbum pulse = new PulseAlbum();
			pulse.Id = Id;

			pulse.Name = Name;
			pulse.Artist = ArtistName;
			pulse.ArtistId = ArtistId;
			pulse.CoverArt = CoverArtId;
			pulse.Year = Year; ;
			pulse.TrackCount = Tracks.Count;
			pulse.Duration = 0;
			foreach (TrackData track in Tracks)
			{
				pulse.Duration += track.DurationSeconds;
			}

			return pulse;
		}
	}

	public class ArtistData : PulseDataObject
	{
		public string Name;

		[JsonIgnore]
		public List<AlbumData> Albums = new List<AlbumData>();

		public DateTime LastPlayed;

		public float WeightedScore;
		public Dictionary<string, float> UserWeightedScore = new Dictionary<string, float>();

		public float GetScore(string userName)
		{
			if (!string.IsNullOrEmpty(userName))
			{
				float userScore;
				if (UserWeightedScore.TryGetValue(userName, out userScore))
				{
					return userScore;
				}
			}
			return WeightedScore;
		}

		public PulseArtist BuildPulse()
		{
			PulseArtist pulse = new PulseArtist();
			pulse.Id = Id;
			pulse.AlbumCount = Albums.Count;
			pulse.Name = Name;
			if (Albums.Count != 0)
			{
				pulse.CoverArt = Albums[0].CoverArtId;
			}
			pulse.AlbumCount = Albums.Count;
			pulse.TrackCount = 0;

			foreach (AlbumData album in Albums)
			{
				pulse.TrackCount += album.Tracks.Count;
			}
			return pulse;
		}

	}

	public class PlaylistData : PulseDataObject
	{
		public string Name;
		public string Comment;
		public List<string> TrackIds;
		public int GetTrackCount()
		{
			return TrackIds.Count;
		}
		public long DurationSeconds;
		public DateTime LastPlayed;
		public Dictionary<string, DateTime> UserLastPlayed = new Dictionary<string, DateTime>();

		public DateTime GetLastPlayed(string userName)
		{
			if (!string.IsNullOrEmpty(userName))
			{
				DateTime userValue;
				if (UserLastPlayed.TryGetValue(userName, out userValue))
				{
					return userValue;
				}
				return default;
			}
			return LastPlayed;
		}

		public PulsePlaylist BuildPulsePlaylist()
		{
			PulsePlaylist pulse = new PulsePlaylist();
			pulse.Id = Id;
			pulse.Name = Name;
			pulse.Comment = Comment;
			pulse.CoverArt = "pl-" + Id;
			pulse.TrackCount = TrackIds.Count;
			pulse.Duration = 0;

			return pulse;
		}

		/// <summary>
		/// If the bad ID is in our track list we'll replace it with the good id
		/// </summary>
		/// <param name="badId"></param>
		/// <param name="goodId"></param>
		public void RepairTrackLinkID(string badId, string goodId)
		{
			for (int i = 0; i < TrackIds.Count; i++)
			{
				if (TrackIds[i] == badId)
				{
					TrackIds[i] = goodId;
					MarkDirty();
				}
			}
		}

		public PlaylistData()
		{
			TrackIds = new List<string>();
			Comment = "";
		}
	}

	public class PulseAnalyticsData : PulseDataObject
	{
		public List<string> RecentlyPlayed = new List<string>();
		public PulseAnalyticsData()
		{
			Id = "analytics";
		}
	}


	public class GenreData : PulseDataObject
	{
		public int TrackCount;
		public int AlbumCount;
		public string Name;
	}

}
