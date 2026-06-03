using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	public class PulsePodcastEpisode : PulseObject
	{
		public string Title;
		public string Description;
		public string StreamId;
		public string CoverArt;
		public string PublishDate;
		public string Status;
		public int Duration;

		public PulsePodcastEpisode()
		{
			Kind = eDataType.PodcastEpisode;
		}
	}

	public class PulsePodcastChannel : PulseObject
	{
		public string Title;
		public string Description;
		public string CoverArt;
		public string Url;
		public string Status;
		public int EpisodeCount;

		public PulsePodcastChannel()
		{
			Kind = eDataType.Podcast;
		}
	}

	public class PulsePodcastDetails : PulseObject
	{
		public PulsePodcastChannel Channel;
		public List<PulsePodcastEpisode> Episodes;

		public PulsePodcastDetails()
		{
			Episodes = new List<PulsePodcastEpisode>();
			Kind = eDataType.PodcastEpisodes;
		}
	}
}
