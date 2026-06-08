using Pulse.DataStorage;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pulse.MusicLibrary
{
	/// <summary>
	/// Runtime in-memory shape. Subtypes carry parent references
	/// (e.g. <c>TrackData.ParentArtist</c>), the
	/// <see cref="m_bIsDirty"/> flag, and transient scoring/analytics state,
	/// and are wired together at startup by <c>WireUpReferences</c> after the
	/// SQLite load. <c>PulseDatabase</c> reads and writes these directly,
	/// column by column; there is no separate on-disk record shape.
	/// </summary>
	public abstract class PulseInfo
	{
		public string Id { get; set; }

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
	
	public class PlaylistAndTracks : PlaylistData
	{
		public List<TrackData> Tracks { get; set; }
		public PlaylistAndTracks(PlaylistData info, List<TrackData> tracks)
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
		public List<ArtistData> Artists { get; set; }
		public List<AlbumData> Albums { get; set; }
		public List<PlaylistData> Playlists { get; set; }
		public List<TrackData> Tracks { get; set; }
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
