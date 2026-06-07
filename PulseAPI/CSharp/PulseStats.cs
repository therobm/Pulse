

using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	public class GenreStat
	{
		public string Genre { get; set; }
		public int Count { get; set; }
		public float Percent { get; set; }
	}

	public class DecadeStat
	{
		public string Decade { get; set; }
		public int Count { get; set; }
		public float Percent { get; set; }
	}

	public class ArtistStat
	{
		public string Name { get; set; }
		public int Value { get; set; }
	}

	public class TrackStat
	{
		public string Title { get; set; }
		public string Artist { get; set; }
		public int Value { get; set; }
	}

	public class FormatStat
	{
		public string Format { get; set; }
		public int Count { get; set; }
		public float Percent { get; set; }
	}

	public class PulseStats : PulseObject
	{
		public int TotalTracks { get; set; }
		public int TotalAlbums { get; set; }
		public int TotalArtists { get; set; }
		public int TotalPlaylists { get; set; }

		public int UnplayedTracks { get; set; }
		public float UnplayedPercent { get; set; }
		public int PlayedOnce { get; set; }
		public int PlayedMultiple { get; set; }

		public int TotalPlaySessions { get; set; }
		public int TotalSkipSessions { get; set; }
		public float SkipPercent { get; set; }

		public long TotalFileSizeBytes { get; set; }
		public int TotalDurationSeconds { get; set; }

		public int StarredTracks { get; set; }
		public int RatedTracks { get; set; }

		public List<GenreStat> GenreBreakdown { get; set; } = new List<GenreStat>();
		public List<DecadeStat> DecadeBreakdown { get; set; } = new List<DecadeStat>();
		public List<ArtistStat> TopArtistsByScore { get; set; } = new List<ArtistStat>();
		public List<ArtistStat> TopArtistsByTracks { get; set; } = new List<ArtistStat>();
		public List<TrackStat> HighestScored { get; set; } = new List<TrackStat>();
		public List<TrackStat> MostSkipped { get; set; } = new List<TrackStat>();
		public List<FormatStat> FormatBreakdown { get; set; } = new List<FormatStat>();

		public PulseStats()
		{
			Kind = eDataType.Stats;
		}
	}
}
