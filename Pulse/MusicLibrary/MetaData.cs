
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pulse.MusicLibrary
{
	/// <summary>
	/// Runtime in-memory shape. Subtypes carry parent references
	/// (e.g. <c>TrackInfo.ParentArtist</c>), the
	/// <see cref="m_bIsDirty"/> flag, transient scoring/analytics state,
	/// and are wired together at startup by
	/// <c>PulseFileDatabase.WireUpReferences</c>. Intentionally distinct
	/// from the <c>*Record</c> types in
	/// <c>Data/PulseFileDataTypes.cs</c> (the on-disk shape) and must
	/// stay that way: in-memory shape is independent of persistence
	/// shape. Cross the boundary via <c>TrackRecord.ToTrackInfo()</c> /
	/// <c>TrackRecord.FromTrackInfo()</c> and the equivalents. Do not
	/// unify the pairs into single classes.
	/// </summary>
	public abstract class PulseInfo
	{
		[JsonIgnore]
		public bool m_bIsDirty = false;
	}

	public class ScoreData
	{
		// PlayCount = times the track was served to the user.
		// SkipCount = times the user rejected the track after it was served.
		// A skip cannot exist without a play; the two are intentionally related, not independent counters.
		public int PlayCount { get; set; }
		public int SkipCount { get; set; }
		public double TotalListenSeconds { get; set; }
		public float WeightedScore { get; set; }
	}
	public class TrackInfo : PulseInfo
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

		//opensubsonic
		public int Rating { get; set; }           // from setRating (0-5)
		public Dictionary<string, bool> Starred { get; set; } = new Dictionary<string, bool>();
		
		public DateTime LastPlayed { get; set; }   // from scrobble

		//smart playlist
		public ScoreData Score { get; set; } = new ScoreData();
		public Dictionary<string, ScoreData> UserScore { get; set; } = new Dictionary<string, ScoreData>();

		[JsonIgnore]
		public ArtistInfo ParentArtist { get; set; }

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

	public class AlbumInfo : PulseInfo
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string ArtistName { get; set; }
		public string ArtistId { get; set; }
		public string Genre { get; set; }
		public string CoverArtId { get; set; }
		public int Year { get; set; }

		public Dictionary<string, bool> Starred { get; set; } = new Dictionary<string, bool>();
		public List<TrackInfo> Tracks { get; set; } = new List<TrackInfo>();
	}

	public class ArtistInfo : PulseInfo
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public Dictionary<string, bool> Starred { get; set; } = new Dictionary<string, bool>();
		public List<AlbumInfo> Albums { get; set; } = new List<AlbumInfo>();

		// Dynamic data populated at runtime
		public float WeightedScore { get; set; }
		public Dictionary<string, float> UserWeightedScore { get; set; } = new Dictionary<string, float>();
	}

	public class PlaylistInfo : PulseInfo
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Comment { get; set; }
		public List<string> TrackIds { get; set; }
		public int GetSongCount()
		{
			return TrackIds.Count;
		}
		public long DurationSeconds { get; set; }

		public PlaylistInfo()
		{
			TrackIds = new List<string>();
			Comment = "";
		}
	}
	public class PulseAnalyticsInfo : PulseInfo
	{
		public List<string> RecentlyPlayed { get; set; } = new List<string>();
		public Dictionary<string, int> ArtistPlayCounts { get; set; } = new Dictionary<string, int>();
	}
}
