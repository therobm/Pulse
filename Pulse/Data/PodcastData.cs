using Microsoft.AspNetCore.Connections.Features;
using Pulse.DataStorage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Pulse.Data
{
	public class PodcastData
	{
		private ConcurrentDictionary<string, Podcast> m_podcasts = new ConcurrentDictionary<string, Podcast>();
		private ConcurrentDictionary<string, Episode> m_episodes = new ConcurrentDictionary<string, Episode>();

		private PulseDataStore m_data;
		private Timer m_saveTimer;

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
				if (episodes[i].DownloadState == eDownloadState.Downloading)
				{
					//we shouldn't save this state, but we'll recover
					episodes[i].DownloadState = eDownloadState.Failed;
				}
				m_episodes[episodes[i].Id] = episodes[i];
			}

			m_saveTimer = new Timer(OnSaveTimer, null, 10000, 10000);
		}

		/// <summary>
		/// Write all dirty podcasts and episodes to the store, then clear their flags.
		/// </summary>
		public void Save()
		{
			List<Podcast> dirtyPodcasts = new List<Podcast>();
			foreach (KeyValuePair<string, Podcast> pair in m_podcasts)
			{
				if (pair.Value.m_bIsDirty)
				{
					dirtyPodcasts.Add(pair.Value);
				}
			}

			List<Episode> dirtyEpisodes = new List<Episode>();
			foreach (KeyValuePair<string, Episode> pair in m_episodes)
			{
				if (pair.Value.m_bIsDirty)
				{
					dirtyEpisodes.Add(pair.Value);
				}
			}

			if (dirtyPodcasts.Count > 0)
			{
				m_data.SaveList(eDataType.Podcast, dirtyPodcasts);
				for (int i = 0; i < dirtyPodcasts.Count; i++)
				{
					dirtyPodcasts[i].m_bIsDirty = false;
				}
			}

			if (dirtyEpisodes.Count > 0)
			{
				m_data.SaveList(eDataType.PodcastEpisode, dirtyEpisodes);
				for (int i = 0; i < dirtyEpisodes.Count; i++)
				{
					dirtyEpisodes[i].m_bIsDirty = false;
				}
			}
		}

		private void OnSaveTimer(object state)
		{
			try
			{
				Save();
			}
			catch (Exception)
			{
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
			podcast.m_bIsDirty = false;
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
			episode.m_bIsDirty = false;
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
			for (int i = 0; i < episodes.Count; i++)
			{
				episodes[i].m_bIsDirty = false;
			}
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
