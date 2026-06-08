using Pulse.DataStorage;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Pulse.Data
{
	public class PodcastData
	{
		private ConcurrentDictionary<string, Podcast> m_podcasts = new ConcurrentDictionary<string, Podcast>();
		private ConcurrentDictionary<string, Episode> m_episodes = new ConcurrentDictionary<string, Episode>();

		private PulseDataStore m_data;

		public PodcastData(PulseConfig config)
		{
			string podcastDB = "podcasts.db";
#if DEBUG
			podcastDB = "podcasts_staging.db";
#endif
			string dbPath = Path.Combine(config.PulseDataPath, podcastDB);
			m_data = new PulseDataStore(dbPath);
		}

		public void Load()
		{
			List<Podcast> podcasts = m_data.LoadList<Podcast>(eDataType.Podcast);
			for (int i = 0; i < podcasts.Count; i++)
			{
				m_podcasts[podcasts[i].Id] = podcasts[i];
			}

			List<Episode> episodes = m_data.LoadList<Episode>(eDataType.PodcastEpisode);
			for (int i = 0; i < episodes.Count; i++)
			{
				m_episodes[episodes[i].Id] = episodes[i];
			}
		}

		public List<Podcast> GetPodcasts()
		{
			return new List<Podcast>(m_podcasts.Values);
		}

		public Podcast LoadPodcast(string podcastId)
		{
			Podcast podcast;
			m_podcasts.TryGetValue(podcastId, out podcast);
			return podcast;
		}

		public void UpdatePodcast(Podcast podcast)
		{
			Podcast existing;
			bool found = m_podcasts.TryGetValue(podcast.Id, out existing);
			if (found)
			{
				podcast.Users = existing.Users;
			}
			m_podcasts[podcast.Id] = podcast;
			m_data.Save(eDataType.Podcast, podcast);
		}

		public void UpdateEpisode(Episode episode)
		{
			Episode existing;
			bool found = m_episodes.TryGetValue(episode.Id, out existing);
			if (found)
			{
				episode.Users = existing.Users;
			}
			m_episodes[episode.Id] = episode;
			m_data.Save(eDataType.PodcastEpisode, episode);
		}

		public void UpdateEpisodes(List<Episode> episodes)
		{
			for (int i = 0; i < episodes.Count; i++)
			{
				Episode episode = episodes[i];
				Episode existing;
				bool found = m_episodes.TryGetValue(episode.Id, out existing);
				if (found)
				{
					episode.Users = existing.Users;
				}
				m_episodes[episode.Id] = episode;
			}
			m_data.SaveList(eDataType.PodcastEpisode, episodes);
		}

		public Episode LoadEpisode(string episodeId)
		{
			Episode episode;
			m_episodes.TryGetValue(episodeId, out episode);
			return episode;
		}

		public List<Episode> LoadEpisodes(string podcastId)
		{
			List<Episode> result = new List<Episode>();
			foreach (KeyValuePair<string, Episode> pair in m_episodes)
			{
				if (pair.Value.PodcastId == podcastId)
				{
					result.Add(pair.Value);
				}
			}
			return result;
		}

		public void Delete(Podcast podcast)
		{
			m_podcasts.TryRemove(podcast.Id, out _);
			m_data.Delete(eDataType.Podcast, podcast.Id);
		}

		public void Delete(Episode episode)
		{
			m_episodes.TryRemove(episode.Id, out _);
			m_data.Delete(eDataType.PodcastEpisode, episode.Id);
		}
	}
}
