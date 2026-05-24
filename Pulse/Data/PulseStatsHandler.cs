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
		public List<ArtistStat> TopArtistsByScore { get; set; } = new List<ArtistStat>();
		public List<ArtistStat> TopArtistsByTracks { get; set; } = new List<ArtistStat>();
		public List<TrackStat> HighestScored { get; set; } = new List<TrackStat>();
		public List<TrackStat> MostSkipped { get; set; } = new List<TrackStat>();
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
			List<PlaylistInfo> allPlaylists,
			string userName)
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
			List<KeyValuePair<string, int>> sortedGenres = new List<KeyValuePair<string, int>>(genreCounts);
			sortedGenres.Sort(CompareCountDescending);
			for (int genreIndex = 0; genreIndex < sortedGenres.Count; genreIndex++)
			{
				KeyValuePair<string, int> pair = sortedGenres[genreIndex];
				stats.GenreBreakdown.Add(new GenreStat
				{
					Genre = pair.Key,
					Count = pair.Value,
					Percent = (pair.Value / trackCountFloat) * 100f
				});
			}

			// Decade breakdown — sorted by decade label ascending, Unknown last
			List<KeyValuePair<string, int>> sortedDecades = new List<KeyValuePair<string, int>>(decadeCounts);
			sortedDecades.Sort(CompareDecadeKeyAscending);
			for (int decadeIndex = 0; decadeIndex < sortedDecades.Count; decadeIndex++)
			{
				KeyValuePair<string, int> pair = sortedDecades[decadeIndex];
				stats.DecadeBreakdown.Add(new DecadeStat
				{
					Decade = pair.Key,
					Count = pair.Value,
					Percent = (pair.Value / trackCountFloat) * 100f
				});
			}

			// Format breakdown
			List<KeyValuePair<string, int>> sortedFormats = new List<KeyValuePair<string, int>>(formatCounts);
			sortedFormats.Sort(CompareCountDescending);
			for (int formatIndex = 0; formatIndex < sortedFormats.Count; formatIndex++)
			{
				KeyValuePair<string, int> pair = sortedFormats[formatIndex];
				stats.FormatBreakdown.Add(new FormatStat
				{
					Format = pair.Key,
					Count = pair.Value,
					Percent = (pair.Value / trackCountFloat) * 100f
				});
			}

			// Top 200 artists by per-user WeightedScore (falls back to global), scaled to 0..100
			int artistLimit = 200;
			List<KeyValuePair<ArtistInfo, float>> artistScored = new List<KeyValuePair<ArtistInfo, float>>();
			for (int idx = 0; idx < allArtists.Count; idx++)
			{
				ArtistInfo artist = allArtists[idx];
				artistScored.Add(new KeyValuePair<ArtistInfo, float>(artist, artist.GetScore(userName)));
			}
			artistScored.Sort(CompareArtistScoredDescending);
			for (int artistIndex = 0; artistIndex < artistScored.Count; artistIndex++)
			{
				KeyValuePair<ArtistInfo, float> pair = artistScored[artistIndex];
				if (artistIndex >= artistLimit || pair.Value <= 0)
				{
					break;
				}
				stats.TopArtistsByScore.Add(new ArtistStat { Name = pair.Key.Name, Value = (int)Math.Round(pair.Value * 100f) });
			}

			// Top artists by track count
			List<KeyValuePair<string, int>> sortedArtistTracks = new List<KeyValuePair<string, int>>(artistTrackCounts);
			sortedArtistTracks.Sort(CompareCountDescending);
			for (int artistIndex = 0; artistIndex < sortedArtistTracks.Count; artistIndex++)
			{
				KeyValuePair<string, int> pair = sortedArtistTracks[artistIndex];
				if (artistIndex >= artistLimit)
				{
					break;
				}
				stats.TopArtistsByTracks.Add(new ArtistStat { Name = pair.Key, Value = pair.Value });
			}

			// Highest scored tracks (per-user score if available, falls back to global), scaled to 0..100
			int trackLimit = 200;
			List<KeyValuePair<TrackInfo, float>> trackScored = new List<KeyValuePair<TrackInfo, float>>();
			for (int idx = 0; idx < allTracks.Count; idx++)
			{
				TrackInfo track = allTracks[idx];
				trackScored.Add(new KeyValuePair<TrackInfo, float>(track, track.GetScore(userName)));
			}
			trackScored.Sort(CompareTrackScoredDescending);
			for (int trackIndex = 0; trackIndex < trackScored.Count; trackIndex++)
			{
				KeyValuePair<TrackInfo, float> pair = trackScored[trackIndex];
				if (trackIndex >= trackLimit || pair.Value <= 0f)
				{
					break;
				}
				stats.HighestScored.Add(new TrackStat
				{
					Title = pair.Key.Title,
					Artist = pair.Key.Artist,
					Value = (int)Math.Round(pair.Value * 100f)
				});
			}

			// Most skipped tracks
			List<TrackInfo> sortedBySkipCount = new List<TrackInfo>(allTracks);
			sortedBySkipCount.Sort(CompareTrackBySkipCountDescending);
			for (int trackIndex = 0; trackIndex < sortedBySkipCount.Count; trackIndex++)
			{
				TrackInfo track = sortedBySkipCount[trackIndex];
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
			}

			return stats;
		}

		private static int CompareCountDescending(KeyValuePair<string, int> left, KeyValuePair<string, int> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		private static int CompareDecadeKeyAscending(KeyValuePair<string, int> left, KeyValuePair<string, int> right)
		{
			string leftKey = left.Key;
			string rightKey = right.Key;
			if (leftKey == "Unknown")
			{
				leftKey = "zzzz";
			}
			if (rightKey == "Unknown")
			{
				rightKey = "zzzz";
			}
			return string.Compare(leftKey, rightKey, StringComparison.Ordinal);
		}

		private static int CompareArtistScoredDescending(KeyValuePair<ArtistInfo, float> left, KeyValuePair<ArtistInfo, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		private static int CompareTrackScoredDescending(KeyValuePair<TrackInfo, float> left, KeyValuePair<TrackInfo, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		private static int CompareTrackBySkipCountDescending(TrackInfo left, TrackInfo right)
		{
			return right.Score.SkipCount.CompareTo(left.Score.SkipCount);
		}

	}
}
