using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Net;
using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using Pulse.DataStorage;

namespace Pulse.Data
{


	public static class PulseStatsBuilder
	{
		public static PulseStats Build(
			List<TrackData> allTracks,
			List<AlbumData> allAlbums,
			List<ArtistData> allArtists,
			List<PlaylistData> allPlaylists,
			string userName)
		{
			PulseStats stats = new PulseStats();

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
				TrackData track = allTracks[i];

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

				bool anyStarred = false;
				foreach (bool isStarred in track.Starred.Values)
				{
					if (isStarred)
					{
						anyStarred = true;
						break;
					}
				}
				if (anyStarred)
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
			List<KeyValuePair<ArtistData, float>> artistScored = new List<KeyValuePair<ArtistData, float>>();
			for (int idx = 0; idx < allArtists.Count; idx++)
			{
				ArtistData artist = allArtists[idx];
				artistScored.Add(new KeyValuePair<ArtistData, float>(artist, artist.GetScore(userName)));
			}
			artistScored.Sort(CompareArtistScoredDescending);
			for (int artistIndex = 0; artistIndex < artistScored.Count; artistIndex++)
			{
				KeyValuePair<ArtistData, float> pair = artistScored[artistIndex];
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
			List<KeyValuePair<TrackData, float>> trackScored = new List<KeyValuePair<TrackData, float>>();
			for (int idx = 0; idx < allTracks.Count; idx++)
			{
				TrackData track = allTracks[idx];
				trackScored.Add(new KeyValuePair<TrackData, float>(track, track.GetScore(userName)));
			}
			trackScored.Sort(CompareTrackScoredDescending);
			for (int trackIndex = 0; trackIndex < trackScored.Count; trackIndex++)
			{
				KeyValuePair<TrackData, float> pair = trackScored[trackIndex];
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
			List<TrackData> sortedBySkipCount = new List<TrackData>(allTracks);
			sortedBySkipCount.Sort(CompareTrackBySkipCountDescending);
			for (int trackIndex = 0; trackIndex < sortedBySkipCount.Count; trackIndex++)
			{
				TrackData track = sortedBySkipCount[trackIndex];
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

		private static int CompareArtistScoredDescending(KeyValuePair<ArtistData, float> left, KeyValuePair<ArtistData, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		private static int CompareTrackScoredDescending(KeyValuePair<TrackData, float> left, KeyValuePair<TrackData, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		private static int CompareTrackBySkipCountDescending(TrackData left, TrackData right)
		{
			return right.Score.SkipCount.CompareTo(left.Score.SkipCount);
		}

	}
}
