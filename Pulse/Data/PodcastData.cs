

using Pulse.DataStorage;
using Pulse.Series;
using System.Collections.Generic;
using System.IO;

namespace Pulse.Data
{
	public class PodcastData
	{
		private PulseDataStore m_podcastData;
		private PulseConfig m_config;
		public PodcastData(PulseConfig config)
		{
			m_config = config;
			string postcastDB = "podcasts.db";
#if DEBUG
			postcastDB = "podcasts_staging.db";
#endif
			string dbPath = Path.Combine(m_config.PulseDataPath, postcastDB);
			m_podcastData = new PulseDataStore(dbPath);
		}

		public List<Podcast> LoadPodcasts()
		{
			return new List<Podcast>();
		}
		public Podcast LoadPodcast(string bookId)
		{
			return null;
		}
		public void UpdatePodcast(Podcast bookData)
		{
		}
		public void UpdateBooks(List<Podcast> books)
		{

		}
		public void UpdateEpisode(Episode Episode)
		{
			List<Episode> Episodes = new List<Episode>();
			Episodes.Add(Episode);
			UpdateEpisodes(Episodes);
		}
		public void UpdateEpisodes(List<Episode> Episodes)
		{
		}
		public Episode LoadEpisode(string EpisodeId)
		{
			return null;
		}
		public List<Episode> LoadEpisodes(string bookId)
		{
			return new List<Episode>();
		}
		public void Delete(Podcast book)
		{

		}
		public void Delete(Episode Episode)
		{
		}
	}
}
