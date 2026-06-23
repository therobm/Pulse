using Pulse.Data;
using Pulse.DataStorage;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.Marshalling;

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

		static Random rand = new Random();
		const int s_artistSpreadWindow = 5;
		const int s_maxTracksPerArtist = 3;

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
			List<TrackData> tracks = new List<TrackData>();

			List<AnaliticUserItem> rankedTracks = m_analyticsData.GetRankedItems(eAnalyticType.Track);
			List<ScoredItem> scoredTracks = new List<ScoredItem>();
			HashSet<string> playedTrackIds = new HashSet<string>();

			for (int i = 0; i < rankedTracks.Count; i++) 
			{
				playedTrackIds.Add(rankedTracks[i].ItemID);

				ScoredItem item = new ScoredItem();
				item.m_object = rankedTracks[i];
				item.m_score = rankedTracks[i].GetScore(m_pulseData, m_analyticsData);
				scoredTracks.Add(item);
			}

			//shuffle scoredTracks by weight
			//pull 40 tracks
			for (int i = 0; i < 40; i++)
			{
				if (i >= scoredTracks.Count)
					break;
				tracks.Add(m_pulseData.GetTrack(scoredTracks[i].m_object.ItemID));
			}

			if (tracks.Count < 50)
			{
				//Continue to fill using shuffled weight distribution
				List<AnaliticUserItem> rankedAlbums = m_analyticsData.GetRankedItems(eAnalyticType.Album);
				List<ScoredItem> scoredAlbums = new List<ScoredItem>();
				for (int i = 0; i < rankedAlbums.Count; i++)
				{
					ScoredItem item = new ScoredItem();
					item.m_object = rankedAlbums[i];
					item.m_score = rankedAlbums[i].GetScore(m_pulseData, m_analyticsData);
					scoredAlbums.Add(item);
				}

				//shuffle scoredAlbums by weight
				//try to fill up the track list
				for (int i = 0; i < scoredAlbums.Count; i++)
				{
					//grab an unplayed track from each album 
					AlbumData album = m_pulseData.GetAlbum(scoredAlbums[i].m_object.ItemID);
					for (int j = 0; j < album.Tracks.Count; j++) 
					{
						string trackId = album.Tracks[i].Id;
						if (!playedTrackIds.Contains(trackId))
						{
							//add this track
							tracks.Add(m_pulseData.GetTrack(trackId));
							break;
						}
					}
				}

				//if we still have open slots, pull random unplayed tracks
				if (tracks.Count < 50)
				{
					//Fill in the remaining 10+ slots with random picks from the database that do not appear in rankedTracks
					List<TrackData> allTracks = m_pulseData.GetAllTracks();
					int tracksRemaining = targetTrackCount - tracks.Count;
					for (int i = 0; i < 1000; i++)
					{
						if (tracks.Count >= 50)
							break;

						int index = rand.Next(allTracks.Count);
						TrackData randTrack = allTracks[index];
						if (!playedTrackIds.Contains(randTrack.Id))
							tracks.Add(randTrack);
					}
				}
			}
			return tracks;
		}
	}
}