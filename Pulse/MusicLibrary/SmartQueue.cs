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
		private class QueueCandidate : IComparable<QueueCandidate>
		{
			public TrackData m_track;
			public int m_playCount;
			public double m_totalPlayedSeconds;
			public DateTime m_lastPlayed = DateTime.MinValue;
			public float m_score;
			public bool m_isUnrated;

			public int CompareTo(QueueCandidate other)
			{
				return other.m_score.CompareTo(m_score);
			}
		}

		private class PoolStat
		{
			public int m_playCount;
			public double m_totalPlayedSeconds;
			public DateTime m_lastPlayed = DateTime.MinValue;
		}

		private const int s_targetDurationSeconds = 5400;
		private const double s_minUnratedFraction = 0.20;
		private const double s_recencyDecayDays = 90.0;
		private const double s_skipPenaltyThreshold = 0.7;
		private const int s_cooldownHours = 72;
		private const int s_artistSpreadWindow = 5;
		private const double s_ratingWeight = 2.0;

		private MusicManager m_musicManager;
		private AnalyticsData m_analyticsData;
		private DateTime m_now;

		public SmartQueue(MusicManager musicManager, AnalyticsData analyticsData)
		{
			m_musicManager = musicManager;
			m_analyticsData = analyticsData;
			m_now = DateTime.UtcNow;
		}

		public List<TrackData> GetTracks(eQueueMode mode, string userId)
		{
			HashSet<string> playedIds = new HashSet<string>();
			List<QueueCandidate> played = RankPlayedTracks(mode, userId, playedIds);
			List<QueueCandidate> unrated = RankUnratedTracks(mode, userId, playedIds);
			List<QueueCandidate> chosen = FillQueueToDuration(played, unrated);
			List<QueueCandidate> ordered = SpaceOutArtists(chosen);

			List<TrackData> tracks = new List<TrackData>();
			for (int index = 0; index < ordered.Count; index++)
			{
				tracks.Add(ordered[index].m_track);
			}
			return tracks;
		}

		private List<QueueCandidate> RankPlayedTracks(eQueueMode mode, string userId, HashSet<string> playedIds)
		{
			List<QueueCandidate> played = new List<QueueCandidate>();

			if (mode == eQueueMode.Popular)
			{
				Dictionary<string, PoolStat> globalPlays = AggregateGlobalPlays();
				foreach (KeyValuePair<string, PoolStat> entry in globalPlays)
				{
					PoolStat stat = entry.Value;
					if (stat.m_playCount <= 0)
					{
						continue;
					}
					playedIds.Add(entry.Key);
					bool inCooldown = stat.m_lastPlayed != DateTime.MinValue && (m_now - stat.m_lastPlayed).TotalHours < s_cooldownHours;
					if (inCooldown)
					{
						continue;
					}
					QueueCandidate candidate = ScoredCandidate(entry.Key, stat.m_playCount, stat.m_totalPlayedSeconds, stat.m_lastPlayed);
					if (candidate != null)
					{
						played.Add(candidate);
					}
				}
				played.Sort();
				return played;
			}

			List<eAnalyticType> trackType = new List<eAnalyticType>();
			trackType.Add(eAnalyticType.Track);
			List<AnaliticUserItem> userItems = m_analyticsData.GetUserItems(userId, trackType);
			for (int index = 0; index < userItems.Count; index++)
			{
				AnaliticUserItem item = userItems[index];
				if (item == null || item.PlayCount <= 0 || string.IsNullOrEmpty(item.ItemID))
				{
					continue;
				}
				playedIds.Add(item.ItemID);
				bool inCooldown = item.LastPlayed != DateTime.MinValue && (m_now - item.LastPlayed).TotalHours < s_cooldownHours;
				if (inCooldown)
				{
					continue;
				}
				QueueCandidate candidate = ScoredCandidate(item.ItemID, item.PlayCount, item.TotalPlayedSeconds, item.LastPlayed);
				if (candidate != null)
				{
					played.Add(candidate);
				}
			}
			played.Sort();

			Dictionary<string, PoolStat> backfillPlays = AggregateGlobalPlays();
			List<QueueCandidate> backfill = new List<QueueCandidate>();
			foreach (KeyValuePair<string, PoolStat> entry in backfillPlays)
			{
				if (playedIds.Contains(entry.Key))
				{
					continue;
				}
				TrackData track = m_musicManager.GetTrack(entry.Key);
				if (track == null || track.DurationSeconds <= 0 || track.Rating == 0)
				{
					continue;
				}
				QueueCandidate candidate = ScoredCandidate(entry.Key, entry.Value.m_playCount, entry.Value.m_totalPlayedSeconds, entry.Value.m_lastPlayed);
				if (candidate != null)
				{
					backfill.Add(candidate);
				}
			}
			backfill.Sort();
			played.AddRange(backfill);
			return played;
		}

		private List<QueueCandidate> RankUnratedTracks(eQueueMode mode, string userId, HashSet<string> playedIds)
		{
			Dictionary<string, int> playsByTrack = new Dictionary<string, int>();
			if (mode == eQueueMode.Popular)
			{
				Dictionary<string, PoolStat> globalPlays = AggregateGlobalPlays();
				foreach (KeyValuePair<string, PoolStat> entry in globalPlays)
				{
					playsByTrack[entry.Key] = entry.Value.m_playCount;
				}
			}
			else
			{
				List<eAnalyticType> trackType = new List<eAnalyticType>();
				trackType.Add(eAnalyticType.Track);
				List<AnaliticUserItem> userItems = m_analyticsData.GetUserItems(userId, trackType);
				for (int index = 0; index < userItems.Count; index++)
				{
					AnaliticUserItem item = userItems[index];
					if (item == null || string.IsNullOrEmpty(item.ItemID))
					{
						continue;
					}
					playsByTrack[item.ItemID] = item.PlayCount;
				}
			}

			Dictionary<string, double> artistAffinity = new Dictionary<string, double>();
			foreach (KeyValuePair<string, int> entry in playsByTrack)
			{
				if (entry.Value <= 0)
				{
					continue;
				}
				TrackData playedTrack = m_musicManager.GetTrack(entry.Key);
				if (playedTrack == null || string.IsNullOrEmpty(playedTrack.ArtistId))
				{
					continue;
				}
				double existing;
				bool present = artistAffinity.TryGetValue(playedTrack.ArtistId, out existing);
				if (present)
				{
					artistAffinity[playedTrack.ArtistId] = existing + entry.Value;
				}
				else
				{
					artistAffinity[playedTrack.ArtistId] = entry.Value;
				}
			}

			List<TrackData> allTracks = m_musicManager.GetAllTracks();
			List<QueueCandidate> unrated = new List<QueueCandidate>();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackData track = allTracks[index];
				if (track == null || track.DurationSeconds <= 0 || track.Rating != 0)
				{
					continue;
				}
				if (playedIds.Contains(track.Id))
				{
					continue;
				}

				double affinityScore = 0;
				if (!string.IsNullOrEmpty(track.ArtistId))
				{
					artistAffinity.TryGetValue(track.ArtistId, out affinityScore);
				}

				QueueCandidate candidate = new QueueCandidate();
				candidate.m_track = track;
				candidate.m_isUnrated = true;
				candidate.m_score = (float)affinityScore;
				unrated.Add(candidate);
			}
			unrated.Sort();
			return unrated;
		}

		private Dictionary<string, PoolStat> AggregateGlobalPlays()
		{
			List<eAnalyticType> trackType = new List<eAnalyticType>();
			trackType.Add(eAnalyticType.Track);
			List<AnaliticUserItem> items = m_analyticsData.GetRankedItems(trackType);

			Dictionary<string, PoolStat> byTrack = new Dictionary<string, PoolStat>();
			for (int index = 0; index < items.Count; index++)
			{
				AnaliticUserItem item = items[index];
				if (item == null || string.IsNullOrEmpty(item.ItemID))
				{
					continue;
				}

				PoolStat stat;
				bool present = byTrack.TryGetValue(item.ItemID, out stat);
				if (!present)
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

		private QueueCandidate ScoredCandidate(string trackId, int playCount, double totalPlayedSeconds, DateTime lastPlayed)
		{
			TrackData track = m_musicManager.GetTrack(trackId);
			if (track == null || track.DurationSeconds <= 0)
			{
				return null;
			}

			double recencyMultiplier = 0;
			if (lastPlayed != DateTime.MinValue)
			{
				double days = (m_now - lastPlayed).TotalDays;
				if (days < 0)
				{
					days = 0;
				}
				recencyMultiplier = 1.0 / (1.0 + days / s_recencyDecayDays);
			}

			double score = playCount * recencyMultiplier;
			score = score + track.Rating * s_ratingWeight * recencyMultiplier;
			if (playCount > 0)
			{
				double averagePlaySeconds = totalPlayedSeconds / playCount;
				double clamped = Math.Clamp(averagePlaySeconds / track.DurationSeconds, 0.0, 1.0);
				double skipRatio = 1.0 - clamped;
				if (skipRatio > s_skipPenaltyThreshold)
				{
					score = score * (1.0 - skipRatio);
				}
			}

			QueueCandidate candidate = new QueueCandidate();
			candidate.m_track = track;
			candidate.m_playCount = playCount;
			candidate.m_totalPlayedSeconds = totalPlayedSeconds;
			candidate.m_lastPlayed = lastPlayed;
			candidate.m_isUnrated = false;
			candidate.m_score = (float)score;
			return candidate;
		}

		private List<QueueCandidate> FillQueueToDuration(List<QueueCandidate> played, List<QueueCandidate> unrated)
		{
			List<QueueCandidate> chosen = new List<QueueCandidate>();
			HashSet<string> usedIds = new HashSet<string>();
			int playedIndex = 0;
			int unratedIndex = 0;
			int unratedChosen = 0;
			double seconds = 0;

			int maxPicks = played.Count + unrated.Count;
			for (int pick = 0; pick < maxPicks; pick++)
			{
				if (seconds >= s_targetDurationSeconds)
				{
					break;
				}
				bool playedLeft = playedIndex < played.Count;
				bool unratedLeft = unratedIndex < unrated.Count;
				if (!playedLeft && !unratedLeft)
				{
					break;
				}

				double requiredUnrated = Math.Ceiling(s_minUnratedFraction * (chosen.Count + 1));
				bool takeUnrated = unratedChosen < requiredUnrated;

				QueueCandidate candidate;
				if (takeUnrated && unratedLeft)
				{
					candidate = unrated[unratedIndex];
					unratedIndex++;
				}
				else if (playedLeft)
				{
					candidate = played[playedIndex];
					playedIndex++;
				}
				else
				{
					candidate = unrated[unratedIndex];
					unratedIndex++;
				}

				if (usedIds.Contains(candidate.m_track.Id))
				{
					continue;
				}
				usedIds.Add(candidate.m_track.Id);
				chosen.Add(candidate);
				seconds = seconds + candidate.m_track.DurationSeconds;
				if (candidate.m_isUnrated)
				{
					unratedChosen++;
				}
			}

			return chosen;
		}

		private List<QueueCandidate> SpaceOutArtists(List<QueueCandidate> chosen)
		{
			List<QueueCandidate> familiar = new List<QueueCandidate>();
			List<QueueCandidate> discovery = new List<QueueCandidate>();
			for (int index = 0; index < chosen.Count; index++)
			{
				if (chosen[index].m_isUnrated)
				{
					discovery.Add(chosen[index]);
				}
				else
				{
					familiar.Add(chosen[index]);
				}
			}

			int total = familiar.Count + discovery.Count;
			double spacing = total + 1;
			if (discovery.Count > 0)
			{
				spacing = (double)total / discovery.Count;
			}

			List<QueueCandidate> ordered = new List<QueueCandidate>();
			int placedDiscovery = 0;
			for (int position = 0; position < total; position++)
			{
				bool takeDiscovery = false;
				if (discovery.Count > 0)
				{
					double discoveryDue = (placedDiscovery + 0.5) * spacing;
					if (position >= discoveryDue)
					{
						takeDiscovery = true;
					}
				}
				if (familiar.Count == 0)
				{
					takeDiscovery = discovery.Count > 0;
				}
				if (discovery.Count == 0)
				{
					takeDiscovery = false;
				}

				List<QueueCandidate> pool = familiar;
				if (takeDiscovery)
				{
					pool = discovery;
				}

				int windowStart = ordered.Count - (s_artistSpreadWindow - 1);
				if (windowStart < 0)
				{
					windowStart = 0;
				}

				int nextIndex = 0;
				for (int candidateIndex = 0; candidateIndex < pool.Count; candidateIndex++)
				{
					string artistId = pool[candidateIndex].m_track.ArtistId;
					bool repeatsArtist = false;
					for (int back = windowStart; back < ordered.Count; back++)
					{
						if (ordered[back].m_track.ArtistId == artistId)
						{
							repeatsArtist = true;
							break;
						}
					}
					if (!repeatsArtist)
					{
						nextIndex = candidateIndex;
						break;
					}
				}

				ordered.Add(pool[nextIndex]);
				pool.RemoveAt(nextIndex);
				if (takeDiscovery)
				{
					placedDiscovery++;
				}
			}

			return ordered;
		}
	}
}
