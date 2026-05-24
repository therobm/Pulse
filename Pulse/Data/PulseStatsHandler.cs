using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Net;
using Pulse.MusicLibrary;

namespace Pulse.Data
{
	public class PulseStatsResponse
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
		public List<ArtistStat> TopArtistsByPlays { get; set; } = new List<ArtistStat>();
		public List<ArtistStat> TopArtistsByTracks { get; set; } = new List<ArtistStat>();
		public List<TrackStat> MostPlayed { get; set; } = new List<TrackStat>();
		public List<TrackStat> MostSkipped { get; set; } = new List<TrackStat>();
		public List<TrackStat> HighestScored { get; set; } = new List<TrackStat>();
		public List<FormatStat> FormatBreakdown { get; set; } = new List<FormatStat>();
	}

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

	public static class PulseStatsBuilder
	{
		public static PulseStatsResponse Build(
			List<TrackInfo> allTracks,
			List<AlbumInfo> allAlbums,
			List<ArtistInfo> allArtists,
			List<PlaylistInfo> allPlaylists)
		{
			PulseStatsResponse stats = new PulseStatsResponse();

			stats.TotalTracks = allTracks.Count;
			stats.TotalAlbums = allAlbums.Count;
			stats.TotalArtists = allArtists.Count;
			stats.TotalPlaylists = allPlaylists.Count;

			if (allTracks.Count == 0)
			{
				return stats;
			}

			// Play state breakdown
			stats.UnplayedTracks = 0;
			stats.PlayedOnce = 0;
			stats.PlayedMultiple = 0;
			stats.TotalPlaySessions = 0;
			stats.TotalSkipSessions = 0;
			stats.StarredTracks = 0;
			stats.RatedTracks = 0;
			stats.TotalFileSizeBytes = 0;
			stats.TotalDurationSeconds = 0;

			Dictionary<string, int> genreCounts = new Dictionary<string, int>();
			Dictionary<string, int> decadeCounts = new Dictionary<string, int>();
			Dictionary<string, int> formatCounts = new Dictionary<string, int>();
			Dictionary<string, int> artistPlayCounts = new Dictionary<string, int>();
			Dictionary<string, int> artistTrackCounts = new Dictionary<string, int>();

			for (int i = 0; i < allTracks.Count; i++)
			{
				TrackInfo track = allTracks[i];

				int playCount = track.Score.PlayCount;
				int skipCount = track.Score.SkipCount;

				if (playCount == 0 && skipCount == 0)
				{
					stats.UnplayedTracks++;
				}
				else if (playCount == 1)
				{
					stats.PlayedOnce++;
				}
				else
				{
					stats.PlayedMultiple++;
				}

				stats.TotalPlaySessions += playCount;
				stats.TotalSkipSessions += skipCount;
				stats.TotalFileSizeBytes += track.FileSizeBytes;
				stats.TotalDurationSeconds += track.DurationSeconds;

				if (track.Starred.Count > 0)
				{
					stats.StarredTracks++;
				}

				if (track.Rating > 0)
				{
					stats.RatedTracks++;
				}

				// Genre
				string genre;
				if (string.IsNullOrEmpty(track.Genre))
				{
					genre = "Unknown";
				}
				else
				{
					genre = track.Genre;
				}
				if (genreCounts.ContainsKey(genre))
				{
					genreCounts[genre]++;
				}
				else
				{
					genreCounts[genre] = 1;
				}

				// Decade
				string decade;
				if (track.Year <= 0)
				{
					decade = "Unknown";
				}
				else
				{
					int decadeStart = (track.Year / 10) * 10;
					decade = decadeStart.ToString() + "s";
				}
				if (decadeCounts.ContainsKey(decade))
				{
					decadeCounts[decade]++;
				}
				else
				{
					decadeCounts[decade] = 1;
				}

				// Format
				string format;
				if (string.IsNullOrEmpty(track.Suffix))
				{
					format = "Unknown";
				}
				else
				{
					format = track.Suffix.ToUpperInvariant();
				}
				if (formatCounts.ContainsKey(format))
				{
					formatCounts[format]++;
				}
				else
				{
					formatCounts[format] = 1;
				}

				// Artist play counts
				string artistName;
				if (string.IsNullOrEmpty(track.Artist))
				{
					artistName = "Unknown";
				}
				else
				{
					artistName = track.Artist;
				}
				if (artistPlayCounts.ContainsKey(artistName))
				{
					artistPlayCounts[artistName] += playCount;
				}
				else
				{
					artistPlayCounts[artistName] = playCount;
				}

				if (artistTrackCounts.ContainsKey(artistName))
				{
					artistTrackCounts[artistName]++;
				}
				else
				{
					artistTrackCounts[artistName] = 1;
				}
			}

			float trackCountFloat = (float)allTracks.Count;
			stats.UnplayedPercent = (stats.UnplayedTracks / trackCountFloat) * 100f;

			int totalSessions = stats.TotalPlaySessions + stats.TotalSkipSessions;
			if (totalSessions > 0)
			{
				stats.SkipPercent = (stats.TotalSkipSessions / (float)totalSessions) * 100f;
			}

			// Genre breakdown — sorted by count descending
			foreach (KeyValuePair<string, int> pair in genreCounts.OrderByDescending(p => p.Value))
			{
				stats.GenreBreakdown.Add(new GenreStat
				{
					Genre = pair.Key,
					Count = pair.Value,
					Percent = (pair.Value / trackCountFloat) * 100f
				});
			}

			// Decade breakdown — sorted by decade label ascending, Unknown last
			foreach (KeyValuePair<string, int> pair in decadeCounts.OrderBy(p => p.Key == "Unknown" ? "zzzz" : p.Key))
			{
				stats.DecadeBreakdown.Add(new DecadeStat
				{
					Decade = pair.Key,
					Count = pair.Value,
					Percent = (pair.Value / trackCountFloat) * 100f
				});
			}

			// Format breakdown
			foreach (KeyValuePair<string, int> pair in formatCounts.OrderByDescending(p => p.Value))
			{
				stats.FormatBreakdown.Add(new FormatStat
				{
					Format = pair.Key,
					Count = pair.Value,
					Percent = (pair.Value / trackCountFloat) * 100f
				});
			}

			// Top 200 artists by play count
			int artistLimit = 200;
			int artistIndex = 0;
			foreach (KeyValuePair<string, int> pair in artistPlayCounts.OrderByDescending(p => p.Value))
			{
				if (artistIndex >= artistLimit || pair.Value == 0)
				{
					break;
				}
				stats.TopArtistsByPlays.Add(new ArtistStat { Name = pair.Key, Value = pair.Value });
				artistIndex++;
			}

			// Top artists by track count
			artistIndex = 0;
			foreach (KeyValuePair<string, int> pair in artistTrackCounts.OrderByDescending(p => p.Value))
			{
				if (artistIndex >= artistLimit)
				{
					break;
				}
				stats.TopArtistsByTracks.Add(new ArtistStat { Name = pair.Key, Value = pair.Value });
				artistIndex++;
			}

			// Most played tracks 
			int trackLimit = 200;
			int trackIndex = 0;
			foreach (TrackInfo track in allTracks.OrderByDescending(t => t.Score.PlayCount))
			{
				if (trackIndex >= trackLimit || track.Score.PlayCount == 0)
				{
					break;
				}
				stats.MostPlayed.Add(new TrackStat
				{
					Title = track.Title,
					Artist = track.Artist,
					Value = track.Score.PlayCount
				});
				trackIndex++;
			}

			// Most skipped tracks 
			trackIndex = 0;
			foreach (TrackInfo track in allTracks.OrderByDescending(t => t.Score.SkipCount))
			{
				if (trackIndex >= trackLimit || track.Score.SkipCount == 0)
				{
					break;
				}
				stats.MostSkipped.Add(new TrackStat
				{
					Title = track.Title,
					Artist = track.Artist,
					Value = track.Score.SkipCount
				});
				trackIndex++;
			}

			// Highest scored tracks 
			trackIndex = 0;
			foreach (TrackInfo track in allTracks.OrderByDescending(t => t.GetScore(null)))
			{

				float score = track.GetScore(null);

				if (trackIndex >= trackLimit || score <= 0f)
				{
					break;
				}
				stats.HighestScored.Add(new TrackStat
				{
					Title = track.Title,
					Artist = track.Artist,
					Value = (int)(score * 100f)
				});
				trackIndex++;
			}

			return stats;
		}
	}
}
