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
		public string Id { get; set; }

		[JsonIgnore]
		public bool m_bIsDirty = false;
	}


	public class TrackData : PulseDataObject
	{
		public class ScoreData
		{
			public int PlayCount { get; set; }
			public int SkipCount { get; set; }
			public double TotalListenSeconds { get; set; }
			public float WeightedScore { get; set; }
		}

		public string LegacyId { get; set; }
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

		[JsonIgnore]
		public ArtistData ParentArtist { get; set; }

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
		public string Name { get; set; }
		public string ArtistName { get; set; }
		public string ArtistId { get; set; }
		public string Genre { get; set; }
		public string CoverArtId { get; set; }
		public int Year { get; set; }

		public Dictionary<string, bool> Starred { get; set; } = new Dictionary<string, bool>();
		public List<TrackData> Tracks { get; set; } = new List<TrackData>();
	}

	public class ArtistData : PulseDataObject
	{
		public string Name { get; set; }
		public Dictionary<string, bool> Starred { get; set; } = new Dictionary<string, bool>();
		public List<AlbumData> Albums { get; set; } = new List<AlbumData>();

		public DateTime LastPlayed { get; set; }  // last time any of this artist's tracks was played

		// Dynamic data populated at runtime
		public float WeightedScore { get; set; }
		public Dictionary<string, float> UserWeightedScore { get; set; } = new Dictionary<string, float>();

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
		public string Name { get; set; }
		public string Comment { get; set; }
		public List<string> TrackIds { get; set; }
		public int GetSongCount()
		{
			return TrackIds.Count;
		}
		public long DurationSeconds { get; set; }
		public DateTime LastPlayed { get; set; }  // aggregate: bumped on every Play / Shuffle, any user
		public Dictionary<string, DateTime> UserLastPlayed { get; set; } = new Dictionary<string, DateTime>();

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
		public List<string> RecentlyPlayed { get; set; } = new List<string>();
		public PulseAnalyticsData()
		{
			Id = "analytics";
		}
	}


	public class GenreData : PulseDataObject
	{
		public int TrackCount { get; set; }
		public int AlbumCount { get; set; }
		public string Name { get; set; }
	}

}
