using Pulse.Data;
using Pulse.DataStorage;
using System;
using System.Collections.Generic;

namespace Pulse.MusicLibrary
{
	public enum eQueueMode
	{
		Personalized,
		Popular
	}

	public class SmartQueue
	{
		private const int s_targetDurationSeconds = 5400;
		private const double s_minUnratedFraction = 0.20;
		private const double s_recencyDecayDays = 90.0;
		private const double s_skipPenaltyThreshold = 0.7;
		private const int s_cooldownHours = 72;
		private const int s_artistSpreadWindow = 5;
		private const double s_ratingWeight = 2.0;

		private class QueueCandidate
		{
			public TrackData m_track;
			public int m_playCount;
			public double m_totalPlayedSeconds;
			public DateTime m_lastPlayed = DateTime.MinValue;
			public float m_score;
			public bool m_isUnrated;
		}

		private class PoolStat
		{
			public int m_playCount;
			public double m_totalPlayedSeconds;
			public DateTime m_lastPlayed = DateTime.MinValue;
		}

		private MusicManager m_musicManager;
		private AnalyticsData m_analyticsData;
		private DateTime m_now;

		private static int CompareCandidateByScoreDescending(QueueCandidate left, QueueCandidate right)
		{
			return right.m_score.CompareTo(left.m_score);
		}

		private static int CompareCandidateByGenreThenScore(QueueCandidate left, QueueCandidate right)
		{
			string leftGenre = left.m_track.Genre;
			string rightGenre = right.m_track.Genre;
			if (leftGenre == null)
			{
				leftGenre = "";
			}
			if (rightGenre == null)
			{
				rightGenre = "";
			}
			int genreCompare = string.Compare(leftGenre, rightGenre, StringComparison.OrdinalIgnoreCase);
			if (genreCompare != 0)
			{
				return genreCompare;
			}
			return right.m_score.CompareTo(left.m_score);
		}

		private List<eAnalyticType> TrackTypeList()
		{
			List<eAnalyticType> types = new List<eAnalyticType>();
			types.Add(eAnalyticType.Track);
			return types;
		}

		private double ComputeRecencyMultiplier(DateTime lastPlayed)
		{
			if (lastPlayed == DateTime.MinValue)
			{
				return 0.0;
			}
			double days = (m_now - lastPlayed).TotalDays;
			if (days < 0)
			{
				days = 0;
			}
			return 1.0 / (1.0 + days / s_recencyDecayDays);
		}

		private bool InCooldown(DateTime lastPlayed)
		{
			if (lastPlayed == DateTime.MinValue)
			{
				return false;
			}
			double hours = (m_now - lastPlayed).TotalHours;
			return hours < s_cooldownHours;
		}

		private float ComputeScore(TrackData track, int playCount, double totalPlayedSeconds, DateTime lastPlayed)
		{
			double recencyMultiplier = ComputeRecencyMultiplier(lastPlayed);
			double score = playCount * recencyMultiplier;
			score = score + track.Rating * s_ratingWeight * recencyMultiplier;

			if (playCount > 0)
			{
				double averagePlaySeconds = totalPlayedSeconds / playCount;
				double fraction = averagePlaySeconds / track.DurationSeconds;
				double clamped = Math.Clamp(fraction, 0.0, 1.0);
				double skipRatio = 1.0 - clamped;
				if (skipRatio > s_skipPenaltyThreshold)
				{
					score = score * (1.0 - skipRatio);
				}
			}

			return (float)score;
		}

		private QueueCandidate BuildScoredCandidate(string trackId, int playCount, double totalPlayedSeconds, DateTime lastPlayed)
		{
			TrackData track = m_musicManager.GetTrack(trackId);
			if (track == null)
			{
				return null;
			}
			if (track.DurationSeconds <= 0)
			{
				return null;
			}

			QueueCandidate candidate = new QueueCandidate();
			candidate.m_track = track;
			candidate.m_playCount = playCount;
			candidate.m_totalPlayedSeconds = totalPlayedSeconds;
			candidate.m_lastPlayed = lastPlayed;
			candidate.m_isUnrated = false;
			candidate.m_score = ComputeScore(track, playCount, totalPlayedSeconds, lastPlayed);
			return candidate;
		}

		private Dictionary<string, PoolStat> AggregateGlobalTrackStats()
		{
			List<AnaliticUserItem> items = m_analyticsData.GetRankedItems(TrackTypeList());
			Dictionary<string, PoolStat> byTrack = new Dictionary<string, PoolStat>();
			for (int index = 0; index < items.Count; index++)
			{
				AnaliticUserItem item = items[index];
				if (item == null || string.IsNullOrEmpty(item.ItemID))
				{
					continue;
				}

				PoolStat stat;
				if (!byTrack.TryGetValue(item.ItemID, out stat))
				{
					stat = new PoolStat();
					byTrack[item.ItemID] = stat;
				}
				stat.m_playCount = stat.m_playCount + item.PlayCount;
				stat.m_totalPlayedSeconds = stat.m_totalPlayedSeconds + item.TotalPlayedSeconds;
				if (item.LastPlayed > stat.m_lastPlayed)
				{
					stat.m_lastPlayed = item.LastPlayed;
				}
			}
			return byTrack;
		}

		private Dictionary<string, double> BuildUserArtistAffinity(List<AnaliticUserItem> userItems)
		{
			Dictionary<string, double> affinity = new Dictionary<string, double>();
			for (int index = 0; index < userItems.Count; index++)
			{
				AnaliticUserItem item = userItems[index];
				if (item == null || item.PlayCount <= 0 || string.IsNullOrEmpty(item.ItemID))
				{
					continue;
				}
				TrackData track = m_musicManager.GetTrack(item.ItemID);
				if (track == null || string.IsNullOrEmpty(track.ArtistId))
				{
					continue;
				}

				double existing;
				if (affinity.TryGetValue(track.ArtistId, out existing))
				{
					affinity[track.ArtistId] = existing + item.PlayCount;
				}
				else
				{
					affinity[track.ArtistId] = item.PlayCount;
				}
			}
			return affinity;
		}

		private Dictionary<string, double> BuildGlobalArtistPopularity(Dictionary<string, PoolStat> globalStats)
		{
			Dictionary<string, double> popularity = new Dictionary<string, double>();
			foreach (KeyValuePair<string, PoolStat> entry in globalStats)
			{
				if (entry.Value.m_playCount <= 0)
				{
					continue;
				}
				TrackData track = m_musicManager.GetTrack(entry.Key);
				if (track == null || string.IsNullOrEmpty(track.ArtistId))
				{
					continue;
				}

				double existing;
				if (popularity.TryGetValue(track.ArtistId, out existing))
				{
					popularity[track.ArtistId] = existing + entry.Value.m_playCount;
				}
				else
				{
					popularity[track.ArtistId] = entry.Value.m_playCount;
				}
			}
			return popularity;
		}

		private QueueCandidate BuildUnratedCandidate(TrackData track, Dictionary<string, double> artistAffinity)
		{
			QueueCandidate candidate = new QueueCandidate();
			candidate.m_track = track;
			candidate.m_playCount = 0;
			candidate.m_totalPlayedSeconds = 0;
			candidate.m_lastPlayed = DateTime.MinValue;
			candidate.m_isUnrated = true;

			double affinityScore = 0;
			if (!string.IsNullOrEmpty(track.ArtistId))
			{
				artistAffinity.TryGetValue(track.ArtistId, out affinityScore);
			}
			candidate.m_score = (float)affinityScore;
			return candidate;
		}

		private List<QueueCandidate> BuildPersonalizedRated(string userId, HashSet<string> userPlayedIds)
		{
			List<AnaliticUserItem> userItems = m_analyticsData.GetUserItems(userId, TrackTypeList());
			List<QueueCandidate> rated = new List<QueueCandidate>();
			for (int index = 0; index < userItems.Count; index++)
			{
				AnaliticUserItem item = userItems[index];
				if (item == null || item.PlayCount <= 0 || string.IsNullOrEmpty(item.ItemID))
				{
					continue;
				}
				userPlayedIds.Add(item.ItemID);
				if (InCooldown(item.LastPlayed))
				{
					continue;
				}

				QueueCandidate candidate = BuildScoredCandidate(item.ItemID, item.PlayCount, item.TotalPlayedSeconds, item.LastPlayed);
				if (candidate != null)
				{
					rated.Add(candidate);
				}
			}
			rated.Sort(CompareCandidateByScoreDescending);

			Dictionary<string, PoolStat> globalStats = AggregateGlobalTrackStats();
			List<QueueCandidate> backfill = new List<QueueCandidate>();
			foreach (KeyValuePair<string, PoolStat> entry in globalStats)
			{
				if (userPlayedIds.Contains(entry.Key))
				{
					continue;
				}
				TrackData track = m_musicManager.GetTrack(entry.Key);
				if (track == null || track.DurationSeconds <= 0)
				{
					continue;
				}
				if (track.Rating == 0)
				{
					continue;
				}

				QueueCandidate candidate = BuildScoredCandidate(entry.Key, entry.Value.m_playCount, entry.Value.m_totalPlayedSeconds, entry.Value.m_lastPlayed);
				if (candidate != null)
				{
					backfill.Add(candidate);
				}
			}
			backfill.Sort(CompareCandidateByScoreDescending);
			rated.AddRange(backfill);
			return rated;
		}

		private List<QueueCandidate> BuildPersonalizedUnrated(string userId, HashSet<string> userPlayedIds)
		{
			List<AnaliticUserItem> userItems = m_analyticsData.GetUserItems(userId, TrackTypeList());
			Dictionary<string, double> artistAffinity = BuildUserArtistAffinity(userItems);

			List<TrackData> allTracks = m_musicManager.GetAllTracks();
			List<QueueCandidate> unrated = new List<QueueCandidate>();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackData track = allTracks[index];
				if (track == null || track.DurationSeconds <= 0)
				{
					continue;
				}
				if (track.Rating != 0)
				{
					continue;
				}
				if (userPlayedIds.Contains(track.Id))
				{
					continue;
				}

				unrated.Add(BuildUnratedCandidate(track, artistAffinity));
			}
			unrated.Sort(CompareCandidateByScoreDescending);
			return unrated;
		}

		private List<QueueCandidate> BuildPopularRated(HashSet<string> globalPlayedIds)
		{
			Dictionary<string, PoolStat> globalStats = AggregateGlobalTrackStats();
			List<QueueCandidate> rated = new List<QueueCandidate>();
			foreach (KeyValuePair<string, PoolStat> entry in globalStats)
			{
				if (entry.Value.m_playCount <= 0)
				{
					continue;
				}
				globalPlayedIds.Add(entry.Key);
				if (InCooldown(entry.Value.m_lastPlayed))
				{
					continue;
				}

				QueueCandidate candidate = BuildScoredCandidate(entry.Key, entry.Value.m_playCount, entry.Value.m_totalPlayedSeconds, entry.Value.m_lastPlayed);
				if (candidate != null)
				{
					rated.Add(candidate);
				}
			}
			rated.Sort(CompareCandidateByScoreDescending);
			return rated;
		}

		private List<QueueCandidate> BuildPopularUnrated(HashSet<string> globalPlayedIds)
		{
			Dictionary<string, PoolStat> globalStats = AggregateGlobalTrackStats();
			Dictionary<string, double> artistPopularity = BuildGlobalArtistPopularity(globalStats);

			List<TrackData> allTracks = m_musicManager.GetAllTracks();
			List<QueueCandidate> unrated = new List<QueueCandidate>();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackData track = allTracks[index];
				if (track == null || track.DurationSeconds <= 0)
				{
					continue;
				}
				if (track.Rating != 0)
				{
					continue;
				}
				if (globalPlayedIds.Contains(track.Id))
				{
					continue;
				}

				unrated.Add(BuildUnratedCandidate(track, artistPopularity));
			}
			unrated.Sort(CompareCandidateByScoreDescending);
			return unrated;
		}

		private List<QueueCandidate> SelectToTarget(List<QueueCandidate> rated, List<QueueCandidate> unrated)
		{
			List<QueueCandidate> selected = new List<QueueCandidate>();
			HashSet<string> usedIds = new HashSet<string>();
			int ratedIndex = 0;
			int unratedIndex = 0;
			int unratedSelected = 0;
			double cumulativeSeconds = 0;

			int maxIterations = rated.Count + unrated.Count;
			for (int iteration = 0; iteration < maxIterations; iteration++)
			{
				if (cumulativeSeconds >= s_targetDurationSeconds)
				{
					break;
				}

				bool ratedAvailable = ratedIndex < rated.Count;
				bool unratedAvailable = unratedIndex < unrated.Count;
				if (!ratedAvailable && !unratedAvailable)
				{
					break;
				}

				double requiredUnrated = Math.Ceiling(s_minUnratedFraction * (selected.Count + 1));
				bool wantUnrated = unratedSelected < requiredUnrated;

				QueueCandidate pick = null;
				if (wantUnrated && unratedAvailable)
				{
					pick = unrated[unratedIndex];
					unratedIndex++;
				}
				else if (ratedAvailable)
				{
					pick = rated[ratedIndex];
					ratedIndex++;
				}
				else
				{
					pick = unrated[unratedIndex];
					unratedIndex++;
				}

				if (pick == null)
				{
					continue;
				}
				if (usedIds.Contains(pick.m_track.Id))
				{
					continue;
				}

				usedIds.Add(pick.m_track.Id);
				selected.Add(pick);
				cumulativeSeconds = cumulativeSeconds + pick.m_track.DurationSeconds;
				if (pick.m_isUnrated)
				{
					unratedSelected++;
				}
			}

			return selected;
		}

		private QueueCandidate RemoveWithArtistSpread(List<QueueCandidate> pool, List<QueueCandidate> placed)
		{
			int windowStart = placed.Count - (s_artistSpreadWindow - 1);
			if (windowStart < 0)
			{
				windowStart = 0;
			}

			for (int index = 0; index < pool.Count; index++)
			{
				string artistId = pool[index].m_track.ArtistId;
				bool collides = false;
				for (int back = windowStart; back < placed.Count; back++)
				{
					if (placed[back].m_track.ArtistId == artistId)
					{
						collides = true;
						break;
					}
				}
				if (!collides)
				{
					QueueCandidate chosen = pool[index];
					pool.RemoveAt(index);
					return chosen;
				}
			}

			QueueCandidate fallback = pool[0];
			pool.RemoveAt(0);
			return fallback;
		}

		private List<QueueCandidate> Sequence(List<QueueCandidate> selected)
		{
			List<QueueCandidate> ratedPool = new List<QueueCandidate>();
			List<QueueCandidate> unratedPool = new List<QueueCandidate>();
			for (int index = 0; index < selected.Count; index++)
			{
				if (selected[index].m_isUnrated)
				{
					unratedPool.Add(selected[index]);
				}
				else
				{
					ratedPool.Add(selected[index]);
				}
			}
			ratedPool.Sort(CompareCandidateByGenreThenScore);
			unratedPool.Sort(CompareCandidateByScoreDescending);

			int total = ratedPool.Count + unratedPool.Count;
			int unratedTotal = unratedPool.Count;
			double step = total + 1;
			if (unratedTotal > 0)
			{
				step = (double)total / unratedTotal;
			}

			List<QueueCandidate> result = new List<QueueCandidate>();
			int placedUnrated = 0;
			for (int position = 0; position < total; position++)
			{
				bool wantUnrated = false;
				if (unratedPool.Count > 0)
				{
					double threshold = (placedUnrated + 0.5) * step;
					if (position >= threshold)
					{
						wantUnrated = true;
					}
				}
				if (ratedPool.Count == 0)
				{
					wantUnrated = unratedPool.Count > 0;
				}
				if (unratedPool.Count == 0)
				{
					wantUnrated = false;
				}

				QueueCandidate chosen;
				if (wantUnrated)
				{
					chosen = RemoveWithArtistSpread(unratedPool, result);
					placedUnrated++;
				}
				else
				{
					chosen = RemoveWithArtistSpread(ratedPool, result);
				}
				result.Add(chosen);
			}

			return result;
		}

		public SmartQueue(MusicManager musicManager, AnalyticsData analyticsData)
		{
			m_musicManager = musicManager;
			m_analyticsData = analyticsData;
			m_now = DateTime.UtcNow;
		}

		public List<TrackData> Build(eQueueMode mode, string userId)
		{
			List<QueueCandidate> rated;
			List<QueueCandidate> unrated;

			if (mode == eQueueMode.Popular)
			{
				HashSet<string> globalPlayedIds = new HashSet<string>();
				rated = BuildPopularRated(globalPlayedIds);
				unrated = BuildPopularUnrated(globalPlayedIds);
			}
			else
			{
				HashSet<string> userPlayedIds = new HashSet<string>();
				rated = BuildPersonalizedRated(userId, userPlayedIds);
				unrated = BuildPersonalizedUnrated(userId, userPlayedIds);
			}

			List<QueueCandidate> selected = SelectToTarget(rated, unrated);
			List<QueueCandidate> ordered = Sequence(selected);

			List<TrackData> result = new List<TrackData>();
			for (int index = 0; index < ordered.Count; index++)
			{
				result.Add(ordered[index].m_track);
			}
			return result;
		}
	}
}
