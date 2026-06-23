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
			public bool m_isUnplayed;

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
		private const double s_minUnplayedFraction = 0.20;
		private const double s_recencyDecayDays = 90.0;
		private const double s_skipPenaltyThreshold = 0.7;
		private const int s_cooldownHours = 72;
		private const int s_artistSpreadWindow = 5;
		private const int s_maxTracksPerArtist = 3;

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
			Dictionary<string, PoolStat> globalPool = AggregateGlobalPlays();
			HashSet<string> playedIds = new HashSet<string>();

			List<QueueCandidate> played = RankPlayedTracks(mode, userId, globalPool, playedIds);
			List<QueueCandidate> unplayed = RankUnplayedTracks(mode, userId, globalPool, playedIds);

			List<QueueCandidate> chosen = FillQueueToDuration(played, unplayed);
			List<QueueCandidate> ordered = SpaceOutArtists(chosen);

			List<TrackData> tracks = new List<TrackData>();
			for (int index = 0; index < ordered.Count; index++)
			{
				tracks.Add(ordered[index].m_track);
			}
			return tracks;
		}

		private List<QueueCandidate> RankPlayedTracks(eQueueMode mode, string userId, Dictionary<string, PoolStat> globalPool, HashSet<string> playedIds)
		{
			List<QueueCandidate> played = new List<QueueCandidate>();

			if (mode == eQueueMode.Popular)
			{
				foreach (KeyValuePair<string, PoolStat> entry in globalPool)
				{
					PoolStat stat = entry.Value;
					if (stat.m_playCount <= 0)
					{
						continue;
					}
					playedIds.Add(entry.Key);
					if (IsInCooldown(stat.m_lastPlayed))
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

			// Personalized: user pool first
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
				if (IsInCooldown(item.LastPlayed))
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

			// Personalized: backfill from global pool
			List<QueueCandidate> backfill = new List<QueueCandidate>();
			foreach (KeyValuePair<string, PoolStat> entry in globalPool)
			{
				if (playedIds.Contains(entry.Key))
				{
					continue;
				}
				PoolStat stat = entry.Value;
				if (stat.m_playCount <= 0)
				{
					continue;
				}
				if (IsInCooldown(stat.m_lastPlayed))
				{
					continue;
				}
				QueueCandidate candidate = ScoredCandidate(entry.Key, stat.m_playCount, stat.m_totalPlayedSeconds, stat.m_lastPlayed);
				if (candidate != null)
				{
					playedIds.Add(entry.Key);
					backfill.Add(candidate);
				}
			}
			backfill.Sort();
			played.AddRange(backfill);
			return played;
		}

		private List<QueueCandidate> RankUnplayedTracks(eQueueMode mode, string userId, Dictionary<string, PoolStat> globalPool, HashSet<string> playedIds)
		{
			// Build artist affinity from the relevant pool
			Dictionary<string, double> artistAffinity = new Dictionary<string, double>();

			if (mode == eQueueMode.Popular)
			{
				foreach (KeyValuePair<string, PoolStat> entry in globalPool)
				{
					if (entry.Value.m_playCount <= 0)
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
						artistAffinity[playedTrack.ArtistId] = existing + entry.Value.m_playCount;
					}
					else
					{
						artistAffinity[playedTrack.ArtistId] = entry.Value.m_playCount;
					}
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
					if (item == null || string.IsNullOrEmpty(item.ItemID) || item.PlayCount <= 0)
					{
						continue;
					}
					TrackData playedTrack = m_musicManager.GetTrack(item.ItemID);
					if (playedTrack == null || string.IsNullOrEmpty(playedTrack.ArtistId))
					{
						continue;
					}
					double existing;
					bool present = artistAffinity.TryGetValue(playedTrack.ArtistId, out existing);
					if (present)
					{
						artistAffinity[playedTrack.ArtistId] = existing + item.PlayCount;
					}
					else
					{
						artistAffinity[playedTrack.ArtistId] = item.PlayCount;
					}
				}
			}

			// Unplayed = not in playedIds (zero plays in the relevant pool)
			List<TrackData> allTracks = m_musicManager.GetAllTracks();
			List<QueueCandidate> unplayed = new List<QueueCandidate>();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackData track = allTracks[index];
				if (track == null || track.DurationSeconds <= 0)
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
				candidate.m_isUnplayed = true;
				candidate.m_score = (float)affinityScore;
				unplayed.Add(candidate);
			}
			unplayed.Sort();
			return unplayed;
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

			double recencyMultiplier = 0.01;
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
			candidate.m_isUnplayed = false;
			candidate.m_score = (float)score;
			return candidate;
		}

		private bool IsInCooldown(DateTime lastPlayed)
		{
			if (lastPlayed == DateTime.MinValue)
			{
				return false;
			}
			return (m_now - lastPlayed).TotalHours < s_cooldownHours;
		}

		private List<QueueCandidate> FillQueueToDuration(List<QueueCandidate> played, List<QueueCandidate> unplayed)
		{
			List<QueueCandidate> chosen = new List<QueueCandidate>();
			HashSet<string> usedIds = new HashSet<string>();
			Dictionary<string, int> artistCounts = new Dictionary<string, int>();
			int playedIndex = 0;
			int unplayedIndex = 0;
			int unplayedChosen = 0;
			double seconds = 0;

			int maxPicks = played.Count + unplayed.Count;
			for (int pick = 0; pick < maxPicks; pick++)
			{
				if (seconds >= s_targetDurationSeconds)
				{
					break;
				}
				bool playedLeft = playedIndex < played.Count;
				bool unplayedLeft = unplayedIndex < unplayed.Count;
				if (!playedLeft && !unplayedLeft)
				{
					break;
				}

				// Lead with a played track, then enforce 20% unplayed going forward
				bool takeUnplayed = false;
				if (chosen.Count > 0 && unplayedLeft)
				{
					double requiredUnplayed = Math.Ceiling(s_minUnplayedFraction * (chosen.Count + 1));
					takeUnplayed = unplayedChosen < requiredUnplayed;
				}

				QueueCandidate candidate = null;
				if (takeUnplayed && unplayedLeft)
				{
					candidate = PickNextValid(unplayed, ref unplayedIndex, usedIds, artistCounts);
				}
				if (candidate == null && playedLeft)
				{
					candidate = PickNextValid(played, ref playedIndex, usedIds, artistCounts);
				}
				if (candidate == null && unplayedLeft)
				{
					candidate = PickNextValid(unplayed, ref unplayedIndex, usedIds, artistCounts);
				}
				if (candidate == null)
				{
					break;
				}

				string artistId = candidate.m_track.ArtistId;
				if (!string.IsNullOrEmpty(artistId))
				{
					int artistCount = 0;
					artistCounts.TryGetValue(artistId, out artistCount);
					artistCounts[artistId] = artistCount + 1;
				}

				usedIds.Add(candidate.m_track.Id);
				chosen.Add(candidate);
				seconds = seconds + candidate.m_track.DurationSeconds;
				if (candidate.m_isUnplayed)
				{
					unplayedChosen++;
				}
			}

			return chosen;
		}

		private QueueCandidate PickNextValid(List<QueueCandidate> pool, ref int poolIndex, HashSet<string> usedIds, Dictionary<string, int> artistCounts)
		{
			while (poolIndex < pool.Count)
			{
				QueueCandidate candidate = pool[poolIndex];
				poolIndex++;

				if (usedIds.Contains(candidate.m_track.Id))
				{
					continue;
				}

				string artistId = candidate.m_track.ArtistId;
				if (!string.IsNullOrEmpty(artistId))
				{
					int artistCount = 0;
					artistCounts.TryGetValue(artistId, out artistCount);
					if (artistCount >= s_maxTracksPerArtist)
					{
						continue;
					}
				}

				return candidate;
			}
			return null;
		}

		private List<QueueCandidate> SpaceOutArtists(List<QueueCandidate> chosen)
		{
			List<QueueCandidate> familiar = new List<QueueCandidate>();
			List<QueueCandidate> discovery = new List<QueueCandidate>();
			for (int index = 0; index < chosen.Count; index++)
			{
				if (chosen[index].m_isUnplayed)
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