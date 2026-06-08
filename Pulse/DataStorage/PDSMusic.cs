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
	}

	public abstract class PulseDataObject
	{
		public string Id;

		[JsonIgnore]
		public bool m_bIsDirty = false;
	}


	public class TrackData : PulseDataObject
	{
		public class ScoreData
		{
			public int PlayCount;
			public int SkipCount;
			public double TotalListenSeconds;
			public float WeightedScore;
		}

		public string LegacyId;
		public string Title;
		public string Artist;
		public string ArtistId;
		public string Album;
		public string AlbumId;
		public string Genre;
		public string FilePath;
		public string CoverArtId;
		public int TrackNumber;
		public int DiscNumber;
		public int Year;
		public int DurationSeconds;
		public long FileSizeBytes;
		public string ContentType;
		public string Suffix;

		public int Rating;
		public Dictionary<string, bool> Starred = new Dictionary<string, bool>();

		public DateTime LastPlayed;

		public ScoreData Score = new ScoreData();
		public Dictionary<string, ScoreData> UserScore = new Dictionary<string, ScoreData>();

		[JsonIgnore]
		public ArtistData ParentArtist;

		public float GetScore(string userName)
		{
			float score = 0;

			if (!string.IsNullOrEmpty(userName) && UserScore.TryGetValue(userName, out ScoreData userData))
			{
				score = userData.WeightedScore;
			}
			else
			{
				score = Score.WeightedScore;
			}

			if (ParentArtist != null)
			{
				if (!string.IsNullOrEmpty(userName) && ParentArtist.UserWeightedScore.TryGetValue(userName, out float artistScore))
				{
					if (artistScore > 0)
					{
						score = score * 0.75f + artistScore * 0.25f;
					}
				}
				else
				{
					if (ParentArtist.WeightedScore > 0)
					{
						score = score * 0.75f + ParentArtist.WeightedScore * 0.25f;
					}
				}
			}
			return score;
		}

		public bool IsStarredBy(string userName)
		{
			if (string.IsNullOrEmpty(userName))
			{
				return false;
			}
			bool starred = false;
			Starred.TryGetValue(userName, out starred);
			return starred;
		}

		public int GetTotalSessions(string userName)
		{
			if (string.IsNullOrEmpty(userName))
			{
				return Score.PlayCount + Score.SkipCount;
			}
			ScoreData data;
			if (UserScore.TryGetValue(userName, out data))
			{
				return data.PlayCount + data.SkipCount;
			}
			return 0;
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

		public Dictionary<string, bool> Starred = new Dictionary<string, bool>();

		[JsonIgnore]
		public List<TrackData> Tracks = new List<TrackData>();
	}

	public class ArtistData : PulseDataObject
	{
		public string Name;
		public Dictionary<string, bool> Starred = new Dictionary<string, bool>();

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
	}

	public class PlaylistData : PulseDataObject
	{
		public string Name;
		public string Comment;
		public List<string> TrackIds;
		public int GetSongCount()
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
