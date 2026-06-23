using Pulse.Data;
using Pulse.DataStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using TagLib.Flac;

namespace Pulse.MusicLibrary
{
	public enum eQueueMode
	{
		Personalized,
		Popular
	}

	public class SmartQueue
	{
		private class ScoredItem : IComparable<ScoredItem>
		{
			public AnaliticUserItem m_object;
			public float m_score;
			public float m_randWeight;

			public int CompareTo(ScoredItem other)
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

		static Random rand = Random.Shared;

		PulseData m_pulseData;
		AnalyticsData m_analyticsData;
		DateTime m_now;

		public SmartQueue(PulseData pulseData, AnalyticsData analyticsData)
		{
			m_pulseData = pulseData;
			m_analyticsData = analyticsData;
			m_now = DateTime.UtcNow;
		}

		public List<TrackData> GetTracks(eQueueMode mode, string userId)
		{
			int targetTrackCount = 50;
			int unplayedTarget = 10;
			List<TrackData> tracks = new List<TrackData>();

			int playedTarget = targetTrackCount - unplayedTarget;
			HashSet<string> rankedTrackIds = new HashSet<string>();
			HashSet<string> addedTracks = new HashSet<string>();

			List<AnaliticUserItem> rankedTracks = GetRankedItems(userId, mode, eAnalyticType.Track);
			List<ScoredItem> scoredTracks = CreateScoredItems(rankedTracks);
			for (int i = 0; i < scoredTracks.Count; i++)
			{
				rankedTrackIds.Add(scoredTracks[i].m_object.ItemID);
			}

			scoredTracks.Sort((a, b) =>
			{
				return b.m_randWeight.CompareTo(a.m_randWeight);
			});

			for (int i = 0; i < playedTarget; i++)
			{
				if (i >= scoredTracks.Count || i >= playedTarget)
					break;

				TrackData track = m_pulseData.GetTrack(scoredTracks[i].m_object.ItemID);
				if (track != null)
				{
					tracks.Add(track);
					addedTracks.Add(track.Id);
				}
			}

			//now fill with unranked tracks starting with album, then artist, then random
			List<AnaliticUserItem> rankedAlbums = GetRankedItems(userId, mode, eAnalyticType.Album);
			List<ScoredItem> scoredAlbums = CreateScoredItems(rankedAlbums);
			scoredAlbums.Sort((a, b) =>
			{
				return b.m_randWeight.CompareTo(a.m_randWeight);
			});
			for (int i = 0; i < scoredAlbums.Count; i++)
			{
				if (tracks.Count >= targetTrackCount)
					break;
				AlbumData album = m_pulseData.GetAlbum(scoredAlbums[i].m_object.ItemID);
				if (album == null) 
					continue;
				for (int j = 0; j < album.Tracks.Count; j++)
				{
					if (rankedTrackIds.Contains(album.Tracks[j].Id))
						continue;
					if (addedTracks.Contains(album.Tracks[j].Id))
						continue;

					tracks.Add(album.Tracks[j]);
					addedTracks.Add(album.Tracks[j].Id);
					break;
				}
			}

			List<AnaliticUserItem> rankedArtists = GetRankedItems(userId, mode, eAnalyticType.Artist);
			List<ScoredItem> scoredArtists = CreateScoredItems(rankedArtists);
			scoredArtists.Sort((a, b) =>
			{
				return b.m_randWeight.CompareTo(a.m_randWeight);
			});
			for (int i = 0; i < scoredArtists.Count; i++)
			{
				if (tracks.Count >= targetTrackCount)
					break;
				ArtistData artist = m_pulseData.GetArtist(scoredArtists[i].m_object.ItemID);
				if (artist == null)
					continue;
				for (int j = 0; j < artist.Albums.Count; j++)
				{
					if (tracks.Count >= targetTrackCount)
						break;
					for (int k = 0; k < artist.Albums[j].Tracks.Count; k++)
					{
						if (rankedTrackIds.Contains(artist.Albums[j].Tracks[k].Id))
							continue;
						if (addedTracks.Contains(artist.Albums[j].Tracks[k].Id))
							continue;
						tracks.Add(artist.Albums[j].Tracks[k]);
						addedTracks.Add(artist.Albums[j].Tracks[k].Id);
						break;
					}
				}
			}

			if (tracks.Count < targetTrackCount)
			{
				List<TrackData> allTracks = m_pulseData.GetAllTracks();
				if (allTracks.Count > 0)
				{
					//fill with random unplayed tracks
					for (int i = 0; i < 1000; i++)
					{
						if (tracks.Count >= targetTrackCount)
							break;

						int index = rand.Next(0, allTracks.Count);
						if (rankedTrackIds.Contains(allTracks[index].Id))
							continue;
						if (addedTracks.Contains(allTracks[index].Id))
							continue;

						tracks.Add(allTracks[index]);
						addedTracks.Add(allTracks[index].Id);
					}
				}
				
			}


			return tracks;
		}

		private List<AnaliticUserItem> GetRankedItems(string userId, eQueueMode mode, eAnalyticType type)
		{
			List<AnaliticUserItem> rankedTracks;
			if (mode == eQueueMode.Popular)
			{
				rankedTracks = m_analyticsData.GetRankedItems(type);
			}
			else
			{
				rankedTracks = m_analyticsData.GetUserItems(userId, type);
			}
			return rankedTracks;
		}

		private List<ScoredItem> CreateScoredItems(List<AnaliticUserItem> items)
		{
			List<ScoredItem> scoredTracks = new List<ScoredItem>();
			for (int index = 0; index < items.Count; index++)
			{
				AnaliticUserItem item = items[index];
				if (item == null || string.IsNullOrEmpty(item.ItemID))
				{
					continue;
				}

				float score = item.GetScore(m_pulseData, m_analyticsData);
				if (score > 0)
				{
					ScoredItem scored = new ScoredItem();
					scored.m_object = item;
					scored.m_score = score;
					scored.m_randWeight = (float)Math.Pow(rand.NextDouble(), 1.0 / score);
					scoredTracks.Add(scored);
				}

			}
			return scoredTracks;
		}
	}
}