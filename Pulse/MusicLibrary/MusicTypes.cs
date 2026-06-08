
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pulse.MusicLibrary
{
	/// <summary>
	/// Runtime in-memory shape. Subtypes carry parent references
	/// (e.g. <c>TrackInfo.ParentArtist</c>), the
	/// <see cref="m_bIsDirty"/> flag, and transient scoring/analytics state,
	/// and are wired together at startup by <c>WireUpReferences</c> after the
	/// SQLite load. <c>PulseDatabase</c> reads and writes these directly,
	/// column by column; there is no separate on-disk record shape.
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
		public string LegacyId { get; set; }
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
				return default(DateTime);
			}
			return LastPlayed;
		}

		public PlaylistInfo()
		{
			TrackIds = new List<string>();
			Comment = "";
		}
	}
	public class PlaylistAndTracks : PlaylistInfo
	{
		public List<TrackInfo> Tracks { get; set; }
		public PlaylistAndTracks(PlaylistInfo info, List<TrackInfo> tracks)
		{
			Tracks = tracks;
			Id = info.Id;
			Name = info.Name;
			Comment = info.Comment;
			TrackIds =info.TrackIds;
			DurationSeconds = info.DurationSeconds;
			LastPlayed = info.LastPlayed;
			UserLastPlayed = info.UserLastPlayed;
		}

	}
	public class PulseAnalyticsInfo : PulseInfo
	{
		public List<string> RecentlyPlayed { get; set; } = new List<string>();
	}

	// Persistent per-user play queue (Subsonic getPlayQueue / savePlayQueue).
	// Written directly to SQLite on save -- no in-memory cache, no dirty flag.
	public class PlayQueueInfo
	{
		public List<string> TrackIds { get; set; } = new List<string>();
		public string CurrentTrackId { get; set; } = "";
		public long PositionMs { get; set; } = 0;
		public DateTime Changed { get; set; }
		public string ChangedBy { get; set; } = "";
	}

	// Persistent per-user bookmark on a track (Subsonic getBookmarks / createBookmark).
	public class BookmarkInfo
	{
		public string TrackId { get; set; } = "";
		public long PositionMs { get; set; } = 0;
		public string Comment { get; set; } = "";
		public DateTime Created { get; set; }
		public DateTime Changed { get; set; }
	}

	public class SearchResult : PulseInfo
	{
		public List<ArtistInfo> Artists { get; set; }
		public List<AlbumInfo> Albums { get; set; }
		public List<PlaylistInfo> Playlists { get; set; }
		public List<TrackInfo> Tracks { get; set; }
		public List<GenreInfo> Genres { get; set; }
	}

	public class GenreInfo : PulseInfo, IComparable<GenreInfo>
	{
		public int TrackCount { get; set; }
		public int AlbumCount { get; set; }
		public string Name { get; set; }

		public int CompareTo(GenreInfo other)
		{
			if (other == null) { return 1; }
			return string.Compare(Name ?? "", other.Name ?? "", StringComparison.OrdinalIgnoreCase);
		}
	}
	// Settings-page record for a single user. Backed by the `users` SQLite
	// table (migration v5, Flatline #201); the activity counters are derived
	// at read time by scanning the in-memory stores.
	public class UserRecord : PulseInfo
	{
		public string Name = "";
		public string DisplayName = "";
		public DateTime Created = DateTime.MinValue;
		public bool IsAdmin = false;
		public int ScoredTrackCount;
		public int StarredCount;
		public int PlaylistLastPlayedCount;
	}
}
